using UnityEngine;

namespace WaterSlides
{
    // The endless ocean follows the descending course. Shoreline props must
    // share its height, otherwise later islands disappear beneath the water.
    [ExecuteAlways, DefaultExecutionOrder(100)]
    public sealed class TropicalIslandWaterline : MonoBehaviour
    {
        [SerializeField] EndlessGround _sea;

        void LateUpdate()
        {
            if (_sea == null) _sea = FindFirstObjectByType<EndlessGround>();
            if (_sea == null) return;
            float waterline = _sea.transform.position.y;
            foreach (Transform island in transform)
            {
                var position = island.position;
                position.y = waterline;
                island.position = position;
            }
        }
    }
}
