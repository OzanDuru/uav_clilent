using UnityEngine;

public class WindArea : MonoBehaviour
{
    [Header("Rüzgar Fiziği")]
    [Tooltip("Rüzgarın hangi yöne iteceği (Örn: X=1 sağa iter)")]
    public Vector3 windDirection = new Vector3(-1f, 0f, 0f); 
    
    [Tooltip("Rüzgarın uçağı ne kadar şiddetli savuracağı")]
    public float windStrength = 15f; 

    // Uçak bu görünmez devasa kutunun içine girdiğinde ve uçmaya devam ettiğinde çalışır
    private void OnTriggerStay(Collider other)
    {
        // Çarpan şeyin uçak olduğundan emin ol (Tag kontrolü)
        if (other.CompareTag("Player"))
        {
            // Uçağı rüzgarın yönüne doğru sürekli ittir (Drift etkisi)
            other.transform.position += windDirection.normalized * windStrength * Time.deltaTime;
        }
    }
}