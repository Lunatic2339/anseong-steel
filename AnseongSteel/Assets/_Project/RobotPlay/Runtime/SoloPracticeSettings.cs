using UnityEngine;

namespace AnseongSteel.RobotPlay
{
    [CreateAssetMenu(menuName="Anseong Steel/Solo practice settings")]
    public sealed class SoloPracticeSettings : ScriptableObject
    {
        public bool startInPractice=true;
        public string scenePath="Assets/_Project/04_Scenes/sunwoo_IntegratedPlay.unity";
        public string sessionObjectName="Robot Combat Authority";
        public SoloPracticePlayerRig playerPrefab;
        public TextAsset rigProfile;
        [Tooltip("Initial tuning offsets in robot-root metres, not measured anatomical anchors.")]
        public Vector3 viewOffset=new Vector3(0,.04f,.12f);
        [Range(.2f,.8f)]public float guardReachFraction=.48f;
        [Min(.2f)] public float pillarWidth=.65f;
        [Range(.3f,3)] public float armReachPerSecond=1.2f;
        [Range(0,.4f)]public float armResponseSeconds=.14f;
    }
}
