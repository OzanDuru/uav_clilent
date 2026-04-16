using UnityEngine;

public class RainController : MonoBehaviour
{
    [Header("Yağmur Sistemi")]
    // RainPrefab3D objesini buraya bağlayacağız
    public DigitalRuby.RainMaker.RainScript rainScript; 

    // Slider her kaydırıldığında bu fonksiyon otomatik çalışacak
    public void ChangeRainIntensity(float newIntensity)
    {
        if (rainScript != null)
        {
            rainScript.RainIntensity = newIntensity; // Yağmurun şiddetini Slider'ın değerine eşitle
        }
    }
}