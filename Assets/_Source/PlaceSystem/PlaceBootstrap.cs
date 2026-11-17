using UnityEngine;

namespace PlaceSystem
{
    /// <summary>Одинаковые условия замера: 60 FPS без vSync и работа без фокуса окна.</summary>
    public sealed class PlaceBootstrap : MonoBehaviour
    {
        [SerializeField] private int targetFrameRate = 60;
        [SerializeField] private float shadowDistance = 160f;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = shadowDistance;
            Application.targetFrameRate = targetFrameRate;
            Application.runInBackground = true;
        }
    }
}
