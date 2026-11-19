using System.Collections.Generic;
using UnityEngine;

namespace RocketSlam.World
{
    /// <summary>
    /// Scene handle of the generated launch-site layout (levels 1 and 2): the seed and the building footprint radii
    /// the generator needs. <see cref="GroundTerrain"/> takes its paint shapes and <see cref="InstancedScenery"/> its
    /// prop instances from <see cref="Layout"/>, which is generated on first use at load and cached per seed.
    /// </summary>
    public class LaunchSite : MonoBehaviour
    {
        [SerializeField] int seed = 4242;
        [SerializeField] List<string> buildingModels = new List<string>();
        [SerializeField] List<float> buildingRadii = new List<float>();

        public int Seed => seed;

        public void Setup(int s, Dictionary<string, float> radii)
        {
            seed = s;
            buildingModels = new List<string>(radii.Keys);
            buildingRadii = new List<float>(radii.Values);
        }

        public float Radius(string model)
        {
            int i = buildingModels.IndexOf(model);
            return i >= 0 ? buildingRadii[i] : 10f;
        }

        public LaunchSiteLayout.Result Layout => LaunchSiteLayout.Get(seed, Radius);
    }
}
