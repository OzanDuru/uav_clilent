// --- 1. KÜTÜPHANELER (KULLANACAĞIMIZ ALET ÇANTALARI) ---
// System: Temel programlama komutlarını (örneğin Exception yani Hata yakalama) içerir.
using System; 
// System.Collections.Generic: İçinde birden fazla veri tutabildiğimiz "List" (Liste) yapısını kullanmamızı sağlar.
using System.Collections.Generic; 
// System.Net.Sockets: Ağ programlamanın kalbidir. İşletim sisteminin ağ iletişim borularına (Soketlere) erişmemizi sağlar.
using System.Net.Sockets; 
// System.Text: Bilgisayarın anladığı 1 ve 0'ları (Byte'ları), insanların anladığı metinlere (String'e) dönüştüren araçları (UTF8) içerir.
using System.Text; 
// UnityEngine: Unity oyun motorunun ana kütüphanesidir. 3D objeler, vektörler, zaman (Time) gibi tüm oyun motoru yeteneklerini buradan alırız.
using UnityEngine; 

using TMPro;// TextMeshPro: Unity'nin gelişmiş metin çizim sistemi. Ekrana yazı yazmak için kullanacağız.           

// --- 2. İŞLETİM SİSTEMİ KONTROLÜ (ÖNİŞLEMCİ DİREKTİFLERİ) ---
// Bu kod derleyiciye (oyunu paketleyen sisteme) şu emri verir: 
// "Eğer bu oyunu Mac (OSX) veya Linux için paketliyorsan, System.Net kütüphanesini de koda dahil et. 
// Çünkü UDS (Unix Domain Sockets) bu işletim sistemlerinde bu kütüphanenin altındadır."
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
using System.Net;
#endif


// --- 3. VERİ YAPILARI (KARGO ŞABLONLARI / JSON TASLAKLARI) ---
// Ağ üzerinden veri gönderirken veriler "JSON" adında evrensel bir düz metin formatında gider ve gelir.
// [Serializable] etiketi Unity'e şu özel yeteneği verir: 
// "Ben bu C# sınıfını sana verdiğimde, bunu otomatik olarak JSON metnine çevirebilmelisin veya tam tersini yapabilmelisin."

[Serializable]
public class PointData 
{ 
    public int id;    // Şehrin numarası
    public float x;   // Haritadaki yatay konumu (Ondalıklı sayı olabilir diye float kullanıyoruz)
    public float z;   // Haritadaki derinlik/ileri konumu (Python'daki 'y' verisi Unity'de 'z' olur)
}

[Serializable]
public class PathResponseMessage 
{ 
    public string type;            // Gelen mesajın tipi (Örn: "GLOBAL_PATH")
    public int mapSize;   
    public float totalCost;         // Haritanın boyutu
    public List<PointData> points; // İçinde birden fazla PointData (Şehir) barındıran tren katarı (Liste)
}

[Serializable]
public class PathRequestMessage 
{ 
    public string type;            // Bizim Python'a göndereceğimiz isteğin tipi (Örn: "GET_GLOBAL_PATH")
}

[Serializable]
public class EmergencyReplanMessage
{
    public string type;     // "EMERGENCY_REPLAN"
    public float currentX;  // Drone'un o anki X konumu
    public float currentZ;  // Drone'un o anki Z konumu
    public float targetX;   // Şu anda gidilen hedefin X konumu
    public float targetZ;   // Şu anda gidilen hedefin Z konumu
    public float obstacleX; // Tehlikeli engelin X konumu
    public float obstacleZ; // Tehlikeli engelin Z konumu
}


// --- 4. ANA OYUN SINIFI (Unity'deki Objemize Bağlanacak Olan Script) ---
// MonoBehaviour: Unity'deki her script'in atasıdır. Bu sayede kodumuz Unity arayüzüne eklenebilir, Start ve Update gibi oyun motoru olaylarını dinleyebilir.
public class DroneNetworkManager : MonoBehaviour
{
    // [Header(...)]: Sadece Unity arayüzünde (Inspector) görsel bir başlık çıkararak ayarları kategorize etmeye yarar. Koda matematiksel bir etkisi yoktur.
    [Header("UDS Bağlantı Ayarları")]
    
    // public: Bu değişkenin Unity arayüzünden değiştirilebilir olmasını sağlar. Kodu açmadan oradan ayar yapabilirsin.
    // socketPath: Python'un dükkan açtığı adres. İkisi birebir aynı olmalı ki iletişim kurabilsinler.
    public string socketPath = "/tmp/unity_berlin52.sock";

    [Header("Dünya Ayarları (Harita Çizimi)")]
    // GameObject: Unity'deki fiziksel objelerdir. cylinderPrefab, şehirleri çizerken kullanacağımız 3D silindir şablonumuzdur.
    public GameObject cylinderPrefab; 
    public float yHeight = 0f;        // Şehirler gökyüzünde değil, tam zeminde (Y=0) dursun.
    public float uniformScale = 1f;   // Koordinatlar çok büyükse haritayı orantılı olarak küçültmek için kullanacağımız matematiksel çarpan.

    [Header("Otonom Drone Ayarları")]
    // Transform: Bir objenin uzaydaki Konumunu, Dönüşünü ve Büyüklüğünü tutan kısımdır. Drone'un konumunu değiştireceğimiz için sadece Transform'unu alıyoruz.
    public Transform myDrone; 
    public float turnSpeed = 120f; // derece/saniye (90-360 arası iyi başlar)        
    public float droneSpeed = 20f;    // Drone saniyede kaç birim uzağa gidecek? (Hız)
    public float flightAltitude = 15f;// Drone yerden kaç metre yüksekte (irtifada) uçacak?
    public float arrivalThreshold = 0.5f; // "Hedefe vardım" demek için hedefe ne kadar yaklaşmamız yeterli? (Kusursuz 0.00'ı tutturmak imkansızdır).

    [Header("UI (Arayüz) Ayarları - TAKSİMETRE")]
    public TextMeshProUGUI estimatedCostText; // Rotanın engelsiz ilk hesaplanan uzunluğu
    public TextMeshProUGUI currentCostText;   // Anlık uçulan mesafe
    public TextMeshProUGUI totalCostText;     // Görev bitince yazılacak final mesafe
    public TextMeshProUGUI rmseText; // RMSE değerini ekranda göstermek için

    private float flownDistance = 0f;
    private Vector3 lastPosition;
    private float currentSpeed;

    // --- 5. ARKA PLAN SİSTEMLERİ (BELLEK VE ŞALTERLER) ---
    // private: Unity arayüzünde GÖRÜNMEZ. Sadece bu kodun kendi içinde kullanacağı arka plan hafızasıdır.
    private Socket activeSocket; // Telefon ahizemiz. Python ile olan canlı bağlantıyı oyun boyunca burada tutacağız.
    private readonly List<GameObject> spawned = new(); // Sahnede yarattığımız şehir objelerinin çöpçü listesi. İleride sahneyi temizlemek istersek diye referanslarını burada tutuyoruz.
    
    // currentRoute: Drone'un beynine yükleyeceğimiz "Uçuş Rotası". İçinde 3 boyutlu GPS koordinatları (Vector3) barındırır.
    private List<Vector3> currentRoute = new List<Vector3>(); 
    
    // currentTargetIndex: Rota listesindeki kaçıncı şehre gittiğimizi aklımızda tuttuğumuz parmak hesabı (0, 1, 2...).
    private int currentTargetIndex = 0; 

    // isFlying: Drone'un motorları çalışıyor mu? Sorusunun cevabı. True (Evet) veya False (Hayır).
    private bool isFlying = false;      

    [Header("Rota Önizleme Ayarları")]
    public LineRenderer routeLine; // Çizgiyi çizecek alet
    public bool waitForInputToFly = true; // Boşluk tuşunu bekleyelim mi?

    [Header("UI (Arayüz) Ayarları - GAZ KOLU")]
    public TextMeshProUGUI speedText; // Ekranda anlık "1.5x" yazması için (İsteğe bağlı)

    // --- RMSE (CROSS-TRACK ERROR) DEĞİŞKENLERİ ---
    private float cteSumOfSquares = 0f;
    private int cteSampleCount = 0;
    private Vector3 lastWaypoint; // Uçağın geldiği son şehir (Çizginin başlangıcı)


    [Header("Görsel Ayarlar")]
    public float cylinderHeight = 40f; // Silindirlerin varsayılan boyu


    [Header("Sensör (Radar) Ayarları")]
    public float radarDistance = 150f; 
    public float safeCorridorWidth = 4f; // Uçağın merkezinden sağa ve sola olan toplam tehlike genişliği (Örn: 15 metre)
    private bool isReplanning = false; // Python'dan cevap gelene kadar tekrar istek yollamayalım
    private float defaultDroneSpeed;
    private float evasionCooldown = 0f; // Kaçış manevrası sonrası sensörü kısa süreliğine kör eder

    

    // --- 6. OYUN MOTORU METOTLARI ---

    // Start(): Unity'de "Play" tuşuna bastığın ilk saniye, sahne yüklenirken sadece ve sadece 1 KERE çalışır. Hazırlık aşamasıdır.
    void Start()
    {
        defaultDroneSpeed = droneSpeed;
        currentSpeed = droneSpeed;
        ConnectToPython(); // 1. Adım: Telefonu kaldırıp Python'u ara.
        
        // 2. Adım: Eğer telefon açıldıysa (Socket null değilse ve bağlandıysa)
        if (activeSocket != null && activeSocket.Connected)
        {
            // Python'a "Ben geldim, bana ACO'nun çözdüğü haritayı yolla" de.
            RequestGlobalPath();
        }
    }

    // Update(): Unity oyun motorunun kalbidir. Oyun oynandığı sürece bilgisayarının hızına göre saniyede ortalama 60-144 kere arka arkaya çalışır. Frame (Kare) yenilenme yeridir.
    void Update()
    {
        // 1. BOŞLUK TUŞU KONTROLÜ (BEKLEME MODU)
        if (!isFlying && currentRoute != null && currentRoute.Count > 0)
        {
            // Eğer boşluk tuşuna basılırsa
            if (Input.GetKeyDown(KeyCode.Space))
            {
                isFlying = true; // Uçuş şalterini aç
                Debug.Log("Motorlar ateşlendi, uçuş başladı!");
                lastPosition = myDrone.position; // Kalkış noktasını kaydet
                flownDistance = 0f;              // Taksimetreyi sıfırla
                if (totalCostText != null) totalCostText.text = "Total Cost: 0.00m";
            }
            else
            {
                // Eğer şalter inikse ve boşluğa basılmadıysa aşağıdaki uçuş matematiğine girme!
                return; 
            }
        }
        // 1. KONTROL: Eğer uçuş şalteri inikse (false), drone objemiz yuvaya konmamışsa veya rotamız boşsa kodu burada durdur (return). Aşağıdaki uçuş matematiğine hiç girme.
        if (!isFlying || myDrone == null || currentRoute == null || currentRoute.Count == 0) return;
        if (evasionCooldown > 0f) { evasionCooldown -= Time.deltaTime; }

        // 2. HEDEFİ SEÇME: Listeden (currentRoute) sıradaki hedefimizin 3D koordinatlarını alıyoruz.
        Vector3 targetPosition = currentRoute[currentTargetIndex];
        // --- RMSE (CROSS-TRACK ERROR) HESAPLAMASI ---
        // Sadece X ve Z eksenlerinde (Kuşbakışı) rotadan ne kadar saptığımıza bakıyoruz
        Vector2 currentPos2D = new Vector2(myDrone.position.x, myDrone.position.z);
        Vector2 startPos2D = new Vector2(lastWaypoint.x, lastWaypoint.z);
        Vector2 targetPos2D = new Vector2(targetPosition.x, targetPosition.z);

        // Teorik rotaya olan dik (yanal) uzaklığımızı bul
        float cte = CalculateCrossTrackError(currentPos2D, startPos2D, targetPos2D);
        
        // Hatanın karesini alıp toplam havuzuna ekle
        cteSumOfSquares += (cte * cte);
        cteSampleCount++;
        // --------------------------------------------
        float targetSpeed = droneSpeed;

        // 3. HAREKET MATEMATİĞİ (En Önemli Kısım)
        // Time.deltaTime: Bir önceki ekran karesinden bu kareye geçene kadar geçen küsuratlı süredir. 
        // Hızı bununla çarparak, donanımdan bağımsız "saniyede 20 birim" hız elde ederiz. Aksi takdirde güçlü bilgisayarda drone mermi gibi uçar.
        // --- GELİŞMİŞ LiDAR RADARI (YELPAZE TARAMA) ---
        
       // --- GELİŞMİŞ LiDAR RADARI (YELPAZE TARAMA + KORİDOR FİLTRESİ) ---
        
        bool tehlikeliEngelVar = false; 
        Vector3 dangerousObstaclePoint = Vector3.zero;
        
        float[] radarAngles = { -30f, -15f, 0f, 15f, 30f }; 

        if (evasionCooldown <= 0f && !isReplanning)
        {
            foreach (float angle in radarAngles)
            {
                Vector3 rayDirection = Quaternion.Euler(0, angle, 0) * myDrone.forward;
                Debug.DrawRay(myDrone.position, rayDirection * radarDistance, Color.cyan); // Taramayı mavi çiz

                RaycastHit hit;
                if (Physics.Raycast(myDrone.position, rayDirection, out hit, radarDistance))
                {
                    if (!hit.collider.gameObject.name.Contains("Hedef"))
                    {
                        // --- SİHİRLİ MATEMATİK (Lateral Distance) ---
                        // Çarpan lazerin uçağın merkez rotasına olan DİKEY (yanal) uzaklığını hesaplıyoruz.
                        float angleRad = Mathf.Abs(angle) * Mathf.Deg2Rad; // Unity Sinüs için Radyan ister
                        float lateralDistance = hit.distance * Mathf.Sin(angleRad);

                        if (hit.distance < 30f)
                        {
                            targetSpeed = defaultDroneSpeed * 0.4f; // Tehlikeye fazla yaklaşıldıysa sert fren
                        }

                        // EĞER ÇARPAN ENGEL BİZİM GÜVENLİ KORİDORUMUZUN İÇİNDEYSE (Çarpışma Kesinse!):
                        if (lateralDistance <= safeCorridorWidth)
                        {
                            tehlikeliEngelVar = true; 
                            dangerousObstaclePoint = hit.point;
                            
                            // Tehlikeli engeli SARI çiz
                            Debug.DrawLine(myDrone.position, hit.point, Color.yellow);
                            Debug.LogWarning($"⚠️ KESİN ÇARPIŞMA ROTASI! {angle} derecede, {hit.distance:F1}m ötede. (Yanal Mesafe: {lateralDistance:F1}m <= {safeCorridorWidth}m)");
                            break;
                        }
                        else
                        {
                            // ENGELİ GÖRDÜK AMA BİZE ÇARPMADAN YANDAN GEÇİP GİDECEK (Teğet geçecek)
                            // RRT* çalıştırmaya gerek yok, zararsız engeli YEŞİL çiz.
                            Debug.DrawLine(myDrone.position, hit.point, Color.green);
                        }
                    }
                }
            }
        }

        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * 3f);
        float step = currentSpeed * Time.deltaTime;
        
        // 1. Hedefin sadece X ve Z'sine doğru git; yükseklik ayrı bir terrain-following katmanında çözülecek.
        Vector3 horizontalTarget = new Vector3(targetPosition.x, myDrone.position.y, targetPosition.z);
        myDrone.position = Vector3.MoveTowards(myDrone.position, horizontalTarget, step);

        // 2. Tam altımızdaki arazinin yüksekliğini ölçüp güvenli irtifayı hesaplıyoruz.
        float currentTerrainY = Terrain.activeTerrain != null
            ? Terrain.activeTerrain.SampleHeight(myDrone.position)
            : 0f;
        float targetAltitude = Mathf.Max(flightAltitude, currentTerrainY + 8f);

        // 3. Y eksenini aniden zıplatmak yerine yumuşakça yeni irtifaya süzdürüyoruz.
        float newY = Mathf.Lerp(myDrone.position.y, targetAltitude, Time.deltaTime * 2f);
        myDrone.position = new Vector3(myDrone.position.x, newY, myDrone.position.z);

        // Drone'un her frame'de gerçekten kat ettiği fiziksel mesafeyi topluyoruz.
        flownDistance += Vector3.Distance(myDrone.position, lastPosition);
        lastPosition = myDrone.position;

        if (currentCostText != null) currentCostText.text = "Current Cost: " + flownDistance.ToString("F2") + "m";

        // 4. ROTASYON (DÖNÜŞ) - Sabit kanatlı uçak gibi roll ve pitch ile banked turn yap.
        Vector3 targetDir = targetPosition - myDrone.position;
        Vector3 flatTargetDir = new Vector3(targetDir.x, 0f, targetDir.z);
        Vector3 flatForward = new Vector3(myDrone.forward.x, 0f, myDrone.forward.z);

        if (flatTargetDir.sqrMagnitude > 0.0001f && flatForward.sqrMagnitude > 0.0001f)
        {
            float turnAngle = Vector3.SignedAngle(flatForward.normalized, flatTargetDir.normalized, Vector3.up);
            float targetRoll = Mathf.Clamp(-turnAngle * 1.5f, -40f, 40f);
            float pitchAngle = Mathf.Clamp((targetPosition.y - myDrone.position.y) * 2f, -20f, 20f);

            Quaternion yawRotation = Quaternion.LookRotation(flatTargetDir.normalized, Vector3.up);
            Quaternion bankedRotation = yawRotation * Quaternion.Euler(pitchAngle, 0f, targetRoll);
            myDrone.rotation = Quaternion.Slerp(myDrone.rotation, bankedRotation, Time.deltaTime * 2f);
        }

        // 5. VARIŞ KONTROLÜ
        // Sabit kanatlı uçuşta hedefe erişimi yatay düzlemde değerlendiriyoruz; Y ekseni terrain-following tarafından ayrı yönetiliyor.
        Vector2 currentXZ = new Vector2(myDrone.position.x, myDrone.position.z);
        Vector2 targetXZ = new Vector2(targetPosition.x, targetPosition.z);
        float distanceToTarget = Vector2.Distance(currentXZ, targetXZ);

        // --- GERÇEK ACİL DURUM ---
        if (tehlikeliEngelVar)
        {
            if (!isReplanning)
            {
                RequestEmergencyPath(dangerousObstaclePoint);
            }
        }
       if (distanceToTarget < arrivalThreshold)
        {
            Debug.Log($"Drone hedef {currentTargetIndex}'e ulaştı...");
            lastWaypoint = currentRoute[currentTargetIndex]; // RMSE: Yeni çizginin başlangıcını kaydet
            currentTargetIndex++; 
        }
        else if (distanceToTarget < 25f && tehlikeliEngelVar)
        {
            Debug.LogWarning($"Hedef {currentTargetIndex} ulaşılamaz görünüyor...");
            lastWaypoint = currentRoute[currentTargetIndex]; // RMSE: Yeni çizginin başlangıcını kaydet
            currentTargetIndex++;
        }

        // Eğer sayacımız, rotadaki toplam şehir sayısına ulaştıysa veya geçtiyse, gidecek yol kalmamıştır.
       // Eğer sayacımız, rotadaki toplam şehir sayısına ulaştıysa veya geçtiyse, gidecek yol kalmamıştır.
        if (currentTargetIndex >= currentRoute.Count)
        {
            isFlying = false; // Motorlar durur
            Debug.Log("GÖREV TAMAMLANDI! Drone tüm rotayı gezdi.");
            
            // --- EKLENEN TEK SATIR BURASI ---
            if (totalCostText != null) totalCostText.text = "Total Cost: " + flownDistance.ToString("F2") + "m";
            // --- FİNAL RMSE HESAPLAMASI ---
            if (cteSampleCount > 0)
            {
                float mse = cteSumOfSquares / cteSampleCount; // Ortalama Kare Hata
                float rmse = Mathf.Sqrt(mse);                 // Kökünü al (RMSE)
                Debug.Log($"[MÜHENDİSLİK METRİĞİ] Yörünge Sapması (Cross-Track) RMSE: {rmse:F4} metre");

                // --- EKRANA YAZDIRMA KISMI (YENİ) ---
                if (rmseText != null)
                {
                    // Ekranda yeşil veya kırmızı renkli çıkması için HTML renk kodları bile kullanabilirsin.
                    rmseText.text = "ε (RMSE): " + rmse.ToString("F2") + "m";
                }
            }
        }
    }

    // --- 7. İLETİŞİM VE HARİTA HAZIRLAMA METOTLARI ---

    void RequestGlobalPath()
    {
        // 1. İSTEK OLUŞTUR: Göndereceğimiz kargonun içine "GET_GLOBAL_PATH" notunu yaz.
        var request = new PathRequestMessage { type = "GET_GLOBAL_PATH" };
        
        // 2. İSTEĞİ GÖNDER: Kuryeye verip (Socket) Python'a yolla.
        SendMessageToPython(request);

        // 3. CEVABI BEKLE VE OKU: Python'un hesaplayıp bize geri yolladığı o JSON metnini teslim al.
        string jsonResponse = ReceiveMessageFromPython();
        
        // 4. ÇEVİRİ (DESERIALIZATION): JsonUtility, o gelen JSON metnini okur ve C# beyninin anlayabileceği "PathResponseMessage" adındaki nesnemize dönüştürür.
        var responseObj = JsonUtility.FromJson<PathResponseMessage>(jsonResponse);
        
        // Eğer çeviri başarılı olduysa ve içinde gerçekten şehirler (points) varsa:
        if (responseObj != null && responseObj.points != null)
        {
            SpawnPoints(responseObj.points);         // Yerdeki silindirleri çiz.
            PrepareFlightRoute(responseObj.points);  // DİKKAT: totalCost parametresini sildik!
        }
    }

    void RequestEmergencyPath(Vector3 obstaclePoint)
    {
        if (activeSocket == null || !activeSocket.Connected || currentTargetIndex >= currentRoute.Count) return;

        isReplanning = true;
        droneSpeed = defaultDroneSpeed * 0.5f; // Yeni rota gelene kadar daha kontrollü ilerleyelim

        try
        {
            Vector3 targetPosition = currentRoute[currentTargetIndex];
            var request = new EmergencyReplanMessage
            {
                type = "EMERGENCY_REPLAN",
                currentX = myDrone.position.x,
                currentZ = myDrone.position.z,
                targetX = targetPosition.x,
                targetZ = targetPosition.z,
                obstacleX = obstaclePoint.x,
                obstacleZ = obstaclePoint.z
            };

            SendEmergencyMessageToPython(request);
            string jsonResponse = ReceiveMessageFromPython();
            var responseObj = JsonUtility.FromJson<PathResponseMessage>(jsonResponse);

            if (responseObj != null && responseObj.points != null && responseObj.points.Count > 0)
            {
                InjectEmergencyRoute(responseObj.points);
                evasionCooldown = 3.0f; // Yeni kaçış rotasına girmesi için sensörü kısa süreliğine devre dışı bırak
                Debug.Log($"Python'dan {responseObj.points.Count} adet geçici kaçış noktası alındı.");
            }
            else
            {
                Debug.LogWarning("Python acil kaçış rotası döndürmedi. Mevcut rota korunuyor.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Acil yeniden planlama başarısız oldu: " + ex.Message);
        }
        finally
        {
            isReplanning = false;
            droneSpeed = defaultDroneSpeed;
        }
    }

    // DÜZELTME: Fonksiyona "float totalCost" parametresi eklendi.
    void PrepareFlightRoute(List<PointData> orderedPoints)
    {
        // Eski bir uçuş planı varsa beyni temizle.
        currentRoute.Clear(); 

        // Python'dan gelen her bir şehir için:
        foreach (var p in orderedPoints)
        {
            float worldX = p.x * uniformScale;
            float worldZ = p.z * uniformScale;
            // Spawn ve hedef şehirler hiçbir zaman dağın içinde kalmasın.
            float terrainY = Terrain.activeTerrain != null
                ? Terrain.activeTerrain.SampleHeight(new Vector3(worldX, 0f, worldZ))
                : 0f;
            float safeY = Mathf.Max(flightAltitude, terrainY + 5f);
            Vector3 flightWaypoint = new Vector3(worldX, safeY, worldZ);
            // Bu uçuş noktasını drone'un seyir defterine ekle.
            currentRoute.Add(flightWaypoint);
        }

        // Eğer seyir defterinde en az 1 nokta varsa uçuş prosedürünü başlat:
        if (currentRoute.Count > 0)
        {
            // DÜZELTME: Oyunu sonsuz döngüye sokan "ReceiveMessageFromPython" çağrıları buradan silindi!

            currentTargetIndex = 0; // İlk hedefe odaklan.
            
            // Sadece doğuş anında dağın içine gömülmeyi engelliyoruz. Sonraki ana rota noktaları sabit irtifada kalır.
            Vector3 spawnPoint = currentRoute[0];
            if (Terrain.activeTerrain != null)
            {
                float terrainY = Terrain.activeTerrain.SampleHeight(new Vector3(spawnPoint.x, 0f, spawnPoint.z));
                if (spawnPoint.y < terrainY)
                {
                    spawnPoint = new Vector3(spawnPoint.x, terrainY + 5f, spawnPoint.z);
                }
            }
            myDrone.position = spawnPoint;
            myDrone.position = spawnPoint;
            lastWaypoint = spawnPoint; // RMSE: İlk çizgimizin başlangıç noktası

            myDrone.GetComponent<TrailRenderer>().Clear(); // Drone'un altından çıkan izlerin (Trail) temizlenmesi. Böylece önceki rotanın izleri yeni rotada görünmez.
            
            // ROTAYI ÇİZGİ OLARAK ÇİZ
            if (routeLine != null)
            {
                RefreshRouteLine();
            }

            // MOTORLARI BEKLEMEYE AL
            if (waitForInputToFly)
            {
                isFlying = false; // Şalteri kapalı tut
                Debug.Log("Rota çizildi! Uçuşu başlatmak için BOŞLUK (Space) tuşuna basın.");
            }
            else
            {
                isFlying = true; 
                Debug.Log("Otonom Uçuş Başlatıldı!");
            }
            
            // --- İŞTE YENİ EKLENEN KISIM: ESTIMATED COST (TAHMİNİ MESAFE) ---
            float initialDistance = 0f;
            for (int i = 0; i < currentRoute.Count - 1; i++) 
            {
                // Çizilen rotadaki her iki nokta arasındaki fiziksel mesafeyi toplar
                initialDistance += Vector3.Distance(currentRoute[i], currentRoute[i + 1]);
            }
            
            // UI Text'e yazdır (Eğer arayüze sürükleyip bıraktıysak)
            if (estimatedCostText != null) 
            {
                estimatedCostText.text = "Estimated Cost: " + initialDistance.ToString("F2") + "m";
            }
            // -----------------------------------------------------------------
        }
    }

    void InjectEmergencyRoute(List<PointData> detourPoints)
    {
        int insertIndex = Mathf.Clamp(currentTargetIndex, 0, currentRoute.Count);

        foreach (var p in detourPoints)
        {
            // RRT* sadece yatay kaçış verir; yükseklik terrain-following katmanında yumuşakça ayarlanır.
            Vector3 detourWaypoint = new Vector3(p.x, myDrone.position.y, p.z);
            currentRoute.Insert(insertIndex, detourWaypoint);
            insertIndex++;
        }

        // Hedef indexi aynı kalır; çünkü yeni noktalar mevcut hedefin önüne eklendi ve drone hemen ilk detour noktasına yönelir.
        currentTargetIndex = Mathf.Clamp(currentTargetIndex, 0, currentRoute.Count - 1);

        RefreshRouteLine();
    }

    // --- 8. AĞ (SOCKET) İLETİŞİM METOTLARI ---

    void ConnectToPython()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        Debug.LogError("Windows'ta UDS sorun yaratabilir. Mac/Linux tercih edilir.");
#else
        try
        {
            // Unix Domain Socket cihazını yaratıyoruz. İnternet olmadan, aynı bilgisayardaki dosyalar arası ultra hızlı iletişim sağlar.
            activeSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var ep = new UnixDomainSocketEndPoint(socketPath);
            activeSocket.Connect(ep); // Kapıyı çal.
            Debug.Log("Python Telsiz Merkezine Bağlanıldı!");
        }
        catch (Exception ex) // Eğer Python dükkanı açmamışsa kod çökmesin, hatayı yakala (catch) ve ekrana yazdır.
        {
            Debug.LogError("Python'a bağlanılamadı. Hata: " + ex.Message);
            activeSocket = null; // Telefon bozuk işaretle.
        }
#endif
    }

    void SendMessageToPython(PathRequestMessage requestObj)
    {
        // Telefon açık değilse hiç göndermeye çalışma, çık.
        if (activeSocket == null || !activeSocket.Connected) return;

        // 1. C# objesini JSON metnine çevir.
        string json = JsonUtility.ToJson(requestObj);
        
        // 2. Metni bilgisayarın donanım dili olan 1 ve 0'lara (Byte dizisine) çevir.
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        
        // 3. FRAMING (ÖNDEN BİLGİLENDİRME): Kargonun boyutunu hesapla ve 4 byte'lık "Little-Endian" şifresine dönüştür. (Python'a "Sana şu kadar veri yolluyorum" demek için).
        byte[] lengthBytes = BitConverter.GetBytes((uint)jsonBytes.Length);

        // 4. Önce kargo fişini (boyutu), hemen ardından asıl kargoyu (json) boruya fırlat.
        activeSocket.Send(lengthBytes);
        activeSocket.Send(jsonBytes);
    }

    void SendEmergencyMessageToPython(EmergencyReplanMessage requestObj)
    {
        if (activeSocket == null || !activeSocket.Connected) return;

        string json = JsonUtility.ToJson(requestObj);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] lengthBytes = BitConverter.GetBytes((uint)jsonBytes.Length);

        activeSocket.Send(lengthBytes);
        activeSocket.Send(jsonBytes);
    }

    string ReceiveMessageFromPython()
    {
        // 1. Borunun ağzında bekle, Python'un önden yolladığı 4 byte'lık kargo fişini (boyutu) al.
        byte[] lenBuf = ReceiveExact(activeSocket, 4);
        // Bu 4 byte'ı matematiksel olarak çarparak asıl sayıya (Örn: 1024 karakter) dönüştür.
        uint len = BitConverter.ToUInt32(lenBuf, 0);

        // 2. Artık boyutunu biliyoruz. Boruya dön ve "Bana tam 1024 byte ver" diyerek asıl JSON'u bekle.
        byte[] jsonBuf = ReceiveExact(activeSocket, (int)len);
        
        // 3. Gelen byte'ları okunabilir metne çevir ve geri döndür.
        return Encoding.UTF8.GetString(jsonBuf);
    }

    // ReceiveExact: Ağ programlamasının "Parçalı Veri" sorununu çözen bekçi köpeğidir.
    byte[] ReceiveExact(Socket sock, int size)
    {
        byte[] buf = new byte[size]; // İstenen boyutta boş raf aç.
        int offset = 0; // Rafın neresinde olduğumuzun imleci.
        
        // İstenen boyut dolana kadar borunun ağzından ayrılma (While döngüsü).
        while (offset < size)
        {
            // Borudan ne kadar su akarsa al ve rafa ekle.
            int read = sock.Receive(buf, offset, size - offset, SocketFlags.None);
            if (read <= 0) throw new Exception("Bağlantı koptu."); // Boru patlarsa hata ver.
            offset += read; // İmleci, okunan veri kadar ileri kaydır.
        }
        return buf; // Ağzına kadar tam dolmuş rafı teslim et.
    }

    // --- 9. YARDIMCI METOTLAR (ÇİZİM VE TEMİZLİK) ---

    void SpawnPoints(List<PointData> points)
    {
        // Önceki izleri temizle
        foreach (var go in spawned) { if (go) Destroy(go); }
        spawned.Clear(); 

        foreach (var p in points)
        {
            float targetX = p.x * uniformScale;
            float targetZ = p.z * uniformScale;

            // 1. Arazinin o noktadaki tam yüksekliğini ölç
            float terrainY = Terrain.activeTerrain.SampleHeight(new Vector3(targetX, 0f, targetZ));

            // 2. Silindirin Boyu: Uzaktan rahatça görünsün diye boyunu 40 yapıyoruz (İstersen değiştirebilirsin)
            // float myCylinderHeight = 40f; 

            // 3. Mükemmel Hizalama: Silindirin merkezi tam ortada olduğu için, yere gömülmemesi adına onu boyunun yarısı kadar (myCylinderHeight) yukarı kaldırıyoruz.
            float finalY = terrainY + cylinderHeight;


            Vector3 finalPos = new Vector3(targetX, finalY, targetZ);

            // 4. Silindiri yarat
            var go = Instantiate(cylinderPrefab, finalPos, Quaternion.identity, this.transform);
            
            // 5. Boyunu ayarla (X ve Z aynı kalıyor, Y ekseninde uzatıyoruz)
            go.transform.localScale = new Vector3(go.transform.localScale.x, cylinderHeight, go.transform.localScale.z);

            go.name = $"Hedef_{p.id}"; 
            spawned.Add(go); 
        }
    }

    void RefreshRouteLine()
    {
        if (routeLine == null) return;

        routeLine.positionCount = currentRoute.Count;
        routeLine.SetPositions(currentRoute.ToArray());
    }

    // OnDestroy(): Unity'de "Play" tuşuna tekrar basıp oyunu durdurduğunda otomatik çalışan çöpçü metodudur.
    void OnDestroy()
    {
        // Eğer telefon ahizesi (soket) açık unutulduysa kapatır.
        // Bu yapılmazsa oyun dursa bile bilgisayar arka planda ağ borusunu açık tutar, RAM (Hafıza) dolar ve oyun çöker (Memory Leak).
        if (activeSocket != null)
        {
            activeSocket.Close();
            Debug.Log("Soket güvenli bir şekilde kapatıldı.");
        }
    }

    // Slider'dan gelen küsuratlı değeri (0.25 ile 4.0 arası) doğrudan oyun hızına eşitler.
    public void SetSimulationSpeed(float newSpeed)
    {
        Time.timeScale = newSpeed;
        
        // Ekranda kaç X hızında olduğumuzu göstermek için:
        if (speedText != null)
        {
            speedText.text = "Speed: " + newSpeed.ToString("F1") + "x";
        }
    }

    // --- 11. SİLİNDİR BOYU KONTROLÜ (UI BUTONLARI İÇİN) ---

    public void IncreaseCylinderHeight()
    {
        cylinderHeight += 1f; // Boyu 1 birim artır
        UpdateAllCylinders();
    }

    public void DecreaseCylinderHeight()
    {
        cylinderHeight -= 1f; // Boyu 1 birim kısalt
        if (cylinderHeight < 0.2f) cylinderHeight = 0.2f; // Yerin altına girmesini ve eksiye düşmesini engelle
        UpdateAllCylinders();
    }

    // Sahnedeki tüm silindirleri bulup boylarını ve konumlarını yeni değere göre güncelleyen arka plan işçisi:
    private void UpdateAllCylinders()
    {
        foreach (var go in spawned)
        {
            if (go != null)
            {
                // Mevcut X ve Z konumunu al
                float x = go.transform.position.x;
                float z = go.transform.position.z;
                
                // Arazinin yüksekliğini tekrar ölç (Çünkü engebeli bir arazi)
                float terrainY = Terrain.activeTerrain.SampleHeight(new Vector3(x, 0f, z));
                
                // Hem pozisyonu hem de boyu yeni cylinderHeight değerine göre güncelle
                go.transform.position = new Vector3(x, terrainY + cylinderHeight, z);
                go.transform.localScale = new Vector3(go.transform.localScale.x, cylinderHeight, go.transform.localScale.z);
            }
        }
    }

    // Verilen bir noktanın, başlangıç ve bitişi belli olan bir çizgiye (rotaya) olan en kısa uzaklığını hesaplar.
    private float CalculateCrossTrackError(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 lineDir = lineEnd - lineStart;
        float lineLenSq = lineDir.sqrMagnitude;
        if (lineLenSq == 0f) return Vector2.Distance(point, lineStart); // Eğer başlangıç ve bitiş aynıysa

        // Noktanın çizgi üzerindeki iz düşümünü (projection) bulur
        float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, lineDir) / lineLenSq);
        Vector2 projection = lineStart + t * lineDir;

        // Gerçek konumumuz ile rotadaki ideal izdüşümümüz arasındaki mesafeyi döndürür
        return Vector2.Distance(point, projection);
    }

}
