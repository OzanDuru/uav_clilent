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

    public TextMeshProUGUI currentCostText; // Sarı UI metni
    private float flownDistance = 0f;
    private Vector3 lastPosition;
    private float distanceMultiplier = 1f; // Hesaplanacak çarpan

    // --- 5. ARKA PLAN SİSTEMLERİ (BELLEK VE ŞALTERLER) ---
    // private: Unity arayüzünde GÖRÜNMEZ. Sadece bu kodun kendi içinde kullanacağı arka plan hafızasıdır.
    private Socket activeSocket; // Telefon ahizemiz. Python ile olan canlı bağlantıyı oyun boyunca burada tutacağız.
    private readonly List<GameObject> spawned = new(); // Sahnede yarattığımız şehir objelerinin çöpçü listesi. İleride sahneyi temizlemek istersek diye referanslarını burada tutuyoruz.
    
    // currentRoute: Drone'un beynine yükleyeceğimiz "Uçuş Rotası". İçinde 3 boyutlu GPS koordinatları (Vector3) barındırır.
    private List<Vector3> currentRoute = new List<Vector3>(); 
    private float targetTotalCost = 0f; // Python'dan gelen asıl skoru oyun sonu için aklımızda tutalım

    // currentTargetIndex: Rota listesindeki kaçıncı şehre gittiğimizi aklımızda tuttuğumuz parmak hesabı (0, 1, 2...).
    private int currentTargetIndex = 0; 

    // isFlying: Drone'un motorları çalışıyor mu? Sorusunun cevabı. True (Evet) veya False (Hayır).
    private bool isFlying = false;      

    [Header("Rota Önizleme Ayarları")]
    public LineRenderer routeLine; // Çizgiyi çizecek alet
    public bool waitForInputToFly = true; // Boşluk tuşunu bekleyelim mi?

    [Header("UI (Arayüz) Ayarları")]
    public TextMeshProUGUI costText; // <-- Ekrana yazdıracağımız metin kutusu

    [Header("Görsel Ayarlar")]
    public float cylinderHeight = 40f; // Silindirlerin varsayılan boyu

    // --- 6. OYUN MOTORU METOTLARI ---

    // Start(): Unity'de "Play" tuşuna bastığın ilk saniye, sahne yüklenirken sadece ve sadece 1 KERE çalışır. Hazırlık aşamasıdır.
    void Start()
    {
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
            }
            else
            {
                // Eğer şalter inikse ve boşluğa basılmadıysa aşağıdaki uçuş matematiğine girme!
                return; 
            }
        }
        // 1. KONTROL: Eğer uçuş şalteri inikse (false), drone objemiz yuvaya konmamışsa veya rotamız boşsa kodu burada durdur (return). Aşağıdaki uçuş matematiğine hiç girme.
        if (!isFlying || myDrone == null || currentRoute == null || currentRoute.Count == 0) return;

        // 2. HEDEFİ SEÇME: Listeden (currentRoute) sıradaki hedefimizin 3D koordinatlarını alıyoruz.
        Vector3 targetPosition = currentRoute[currentTargetIndex];

        // 3. HAREKET MATEMATİĞİ (En Önemli Kısım)
        // Time.deltaTime: Bir önceki ekran karesinden bu kareye geçene kadar geçen küsuratlı süredir. 
        // Hızı bununla çarparak, donanımdan bağımsız "saniyede 20 birim" hız elde ederiz. Aksi takdirde güçlü bilgisayarda drone mermi gibi uçar.
        float step = droneSpeed * Time.deltaTime;
        
        // MoveTowards: Bir noktadan (mevcut konum), diğer bir noktaya (hedef), belirli bir adım büyüklüğünde (step) ilerlemenin Unity'deki matematiksel formülüdür.
        myDrone.position = Vector3.MoveTowards(myDrone.position, targetPosition, step);
        // --- TAKSİMETRE MATEMATİĞİ ---
        float frameDist = Vector3.Distance(myDrone.position, lastPosition);
        flownDistance += (frameDist * distanceMultiplier); // Çarpanla büyüt
        lastPosition = myDrone.position;

        if (currentCostText != null) currentCostText.text = "Current Cost: " + flownDistance.ToString("F2") + "m";

        // 4. ROTASYON (DÖNÜŞ): Drone'un ön burnunu milimetrik olarak gideceği noktaya (targetPosition) doğru çevir. Yengeç gibi yan yan uçmasını engeller.
        //myDrone.LookAt(targetPosition);
        // 4. ROTASYON (DÖNÜŞ) - Smooth / Yavaş dönüş
        Vector3 dir = (targetPosition - myDrone.position);
        dir.y = 0f; // İstersen drone sadece yatay düzlemde dönsün (pitch yapmasın)

        if (dir.sqrMagnitude > 0.0001f) // sıfır vektör hatasını önle
        {
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);

            myDrone.rotation = Quaternion.RotateTowards(
                myDrone.rotation,
                targetRot,
                turnSpeed * Time.deltaTime // bu frame’de dönebileceği max derece
            );
        }

        // 5. VARIŞ KONTROLÜ (PİSAGOR TEOREMİ)
        // Vector3.Distance: İki nokta (drone konumu ile hedef konumu) arasındaki kuş uçuşu mesafeyi (uzaklığı) ölçer.
        float distanceToTarget = Vector3.Distance(myDrone.position, targetPosition);

        // Eğer aramızdaki mesafe, bizim belirlediğimiz eşikten (0.5 metre) daha küçükse:
        if (distanceToTarget < arrivalThreshold)
        {
            Debug.Log($"Drone hedef {currentTargetIndex}'e ulaştı.Koordinat: {targetPosition} Sıradakine geçiliyor...");
            
            // Sıradaki hedefe geçmek için sayacı 1 artır. (Örn: 0. şehirden 1. şehre geç)
            currentTargetIndex++; 

            // Eğer sayacımız, rotadaki toplam şehir sayısına ulaştıysa veya geçtiyse, gidecek yol kalmamıştır.
            if (currentTargetIndex >= currentRoute.Count)
            {
                isFlying = false; // Motorlar durur
                Debug.Log("GÖREV TAMAMLANDI! Drone tüm rotayı gezdi.");
                
                // --- İŞTE BU SATIRI EKLE (FİNAL EŞİTLEMESİ) ---
                if (currentCostText != null) currentCostText.text = "Current Cost: " + targetTotalCost.ToString("F2") + "m";
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
            // Eğer ekranda bir metin kutusu seçiliyse, Cost değerini yazdır
            if (costText != null)
            {
                // .ToString("F2") kısmı, sayının virgülden sonra sadece 2 hanesini gösterir (Örn: 7548.99)
                costText.text = "Total Cost: " + responseObj.totalCost.ToString("F2") + "m";
            }
            SpawnPoints(responseObj.points);         // Yerdeki silindirleri çiz.
            targetTotalCost = responseObj.totalCost;
            PrepareFlightRoute(responseObj.points, responseObj.totalCost);  // DÜZELTME: totalCost parametre olarak gönderiliyor.
        }
    }

    // DÜZELTME: Fonksiyona "float totalCost" parametresi eklendi.
    void PrepareFlightRoute(List<PointData> orderedPoints, float totalCost)
    {
        // Eski bir uçuş planı varsa beyni temizle.
        currentRoute.Clear(); 

        // Python'dan gelen her bir şehir için:
        foreach (var p in orderedPoints)
        {
            // Şehrin tam tepesinde, bizim belirlediğimiz "flightAltitude" (Örn: 15 metre) yüksekliğinde 3 boyutlu yeni bir GPS noktası yarat.
            Vector3 flightWaypoint = new Vector3(p.x * uniformScale, flightAltitude, p.z * uniformScale);
            // Bu uçuş noktasını drone'un seyir defterine ekle.
            currentRoute.Add(flightWaypoint);
        }

        // Eğer seyir defterinde en az 1 nokta varsa uçuş prosedürünü başlat:
        if (currentRoute.Count > 0)
        {
            // DÜZELTME: Oyunu sonsuz döngüye sokan "ReceiveMessageFromPython" çağrıları buradan silindi!

            currentTargetIndex = 0; // İlk hedefe odaklan.
            
            // Oyunu başlatır başlatmaz drone'u yavaşça yerden kaldırmak yerine, direkt rotanın ilk noktasında havaya ışınlıyoruz (Teleport).
            myDrone.position = currentRoute[0]; 

            myDrone.GetComponent<TrailRenderer>().Clear(); // Drone'un altından çıkan izlerin (Trail) temizlenmesi. Böylece önceki rotanın izleri yeni rotada görünmez.
            
            // ROTAYI ÇİZGİ OLARAK ÇİZ
            if (routeLine != null)
            {
                routeLine.positionCount = currentRoute.Count; // Nokta sayısını ayarla
                routeLine.SetPositions(currentRoute.ToArray()); // Koordinatları ver
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
            
            // SİHİRLİ ÇARPAN HESAPLAMASI
            float physicalDist = 0f;
            for (int i = 0; i < currentRoute.Count - 1; i++) physicalDist += Vector3.Distance(currentRoute[i], currentRoute[i + 1]);
            
            // Python'dan gelen orijinal Cost'u, Unity'deki mesafeye bölüyoruz. (DÜZELTME: Parametre olan totalCost kullanıldı).
            if (physicalDist > 0.01f) distanceMultiplier = totalCost / physicalDist;
                // // Motorları çalıştır (Update döngüsündeki kilit açılır).
                // isFlying = true; 
                // Debug.Log("Otonom Uçuş Başlatıldı!");
        }
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

    // --- 10. SİMÜLASYON HIZ KONTROLÜ (UI BUTONLARI İÇİN) ---
     public void SetSpeedSlow()
    {
        Time.timeScale = 0.25f; // Yarı Hızlı
        Debug.Log("Simülasyon Hızı: 0.25x (Yarı Hızlı)");
    }
    public void SetSpeedNormal()
    {
        Time.timeScale = 1f; // Normal Zaman
        Debug.Log("Simülasyon Hızı: 1x (Normal)");
    }

    public void SetSpeedFast()
    {
        Time.timeScale = 2f; // 2 Kat Hızlı
        Debug.Log("Simülasyon Hızı: 2x (Hızlı)");
    }

    public void SetSpeedVeryFast()
    {
        Time.timeScale = 4f; // 4 Kat Hızlı
        Debug.Log("Simülasyon Hızı: 4x (Çok Hızlı)");
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
}