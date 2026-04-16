using TMPro;
using UnityEngine;

public class DroneConeScanner : MonoBehaviour
{
    [Header("Scan Settings")]
    [Min(0.1f)] public float scanRange = 100f;
    [Range(1f, 179f)] public float coneAngle = 30f;
    [Min(1)] public int rayResolution = 64;

    [Header("Surface Scan")]
    public Transform scanOrigin;
    public LayerMask terrainMask = ~0;
    public bool useLocalDownDirection = true;

    [Header("UI")]
    public TextMeshProUGUI scannedAreaText;

    private const float UiRefreshInterval = 1f;
    private float uiRefreshTimer;
    private Vector3[] lastHitPoints = new Vector3[0];
    private int lastHitCount;

    private void Start()
    {
        UpdateScannedAreaText();
    }

    private void Update()
    {
        PerformConeScan();

        uiRefreshTimer += Time.deltaTime;
        if (uiRefreshTimer >= UiRefreshInterval)
        {
            uiRefreshTimer = 0f;
            UpdateScannedAreaText();
        }
    }

    private void PerformConeScan()
    {
        Transform source = scanOrigin != null ? scanOrigin : transform;
        Vector3 origin = source.position;
        Vector3 scanAxis = useLocalDownDirection ? -source.up : source.forward;
        GetScanBasis(source, scanAxis, out Vector3 basisRight, out Vector3 basisUp);

        float baseRadius = GetConeBaseRadius();
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

        EnsureHitPointBuffer();
        lastHitCount = 0;

        for (int i = 0; i < rayResolution; i++)
        {
            Vector2 diskPoint = GetFibonacciDiskPoint(i, rayResolution, goldenAngle);
            Vector3 targetPoint = origin + scanAxis * scanRange + basisRight * (diskPoint.x * baseRadius) + basisUp * (diskPoint.y * baseRadius);
            Vector3 direction = (targetPoint - origin).normalized;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, scanRange, terrainMask, QueryTriggerInteraction.Ignore))
            {
                Debug.DrawRay(origin, direction * hit.distance, Color.red);
                lastHitPoints[lastHitCount] = hit.point;
                lastHitCount++;
            }
        }
    }

    private void EnsureHitPointBuffer()
    {
        if (lastHitPoints.Length != rayResolution)
        {
            lastHitPoints = new Vector3[rayResolution];
        }
    }

    private void GetScanBasis(Transform source, Vector3 scanAxis, out Vector3 basisRight, out Vector3 basisUp)
    {
        basisRight = source.right;
        if (Mathf.Abs(Vector3.Dot(basisRight.normalized, scanAxis.normalized)) > 0.98f)
        {
            basisRight = source.forward;
        }

        basisUp = Vector3.Cross(scanAxis, basisRight).normalized;
        basisRight = Vector3.Cross(basisUp, scanAxis).normalized;
    }

    private Vector2 GetFibonacciDiskPoint(int index, int count, float goldenAngle)
    {
        if (count <= 1)
        {
            return Vector2.zero;
        }

        float normalizedRadius = Mathf.Sqrt(index / (float)(count - 1));
        float angle = index * goldenAngle;

        return new Vector2(
            Mathf.Cos(angle) * normalizedRadius,
            Mathf.Sin(angle) * normalizedRadius
        );
    }

    private float GetConeBaseRadius()
    {
        return Mathf.Tan(coneAngle * 0.5f * Mathf.Deg2Rad) * scanRange;
    }

    private float GetCoverageArea()
    {
        if (lastHitCount < 3)
        {
            return 0f;
        }

        Vector2 center = Vector2.zero;
        for (int i = 0; i < lastHitCount; i++)
        {
            center += new Vector2(lastHitPoints[i].x, lastHitPoints[i].z);
        }

        center /= lastHitCount;

        Vector2[] points2D = new Vector2[lastHitCount];
        for (int i = 0; i < lastHitCount; i++)
        {
            points2D[i] = new Vector2(lastHitPoints[i].x, lastHitPoints[i].z);
        }

        System.Array.Sort(points2D, (a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - center.y, a.x - center.x);
            float angleB = Mathf.Atan2(b.y - center.y, b.x - center.x);
            return angleA.CompareTo(angleB);
        });

        float area = 0f;
        for (int i = 0; i < points2D.Length; i++)
        {
            Vector2 current = points2D[i];
            Vector2 next = points2D[(i + 1) % points2D.Length];
            area += current.x * next.y - next.x * current.y;
        }

        return Mathf.Abs(area) * 0.5f;
    }

    private void UpdateScannedAreaText()
    {
        if (scannedAreaText == null)
        {
            return;
        }

        scannedAreaText.text = $"Taranan Alan: {GetCoverageArea():F0} m²";
    }
}
