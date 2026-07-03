using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WaterSlides
{
    public static class WaterSlidePhysicsMaterials
    {
        const string SlideMaterialPath = "Assets/PhysicsMaterial/Slide.physicMaterial";

        static PhysicsMaterial _slide;

        public static PhysicsMaterial Slide
        {
            get
            {
                if (_slide != null)
                    return _slide;

#if UNITY_EDITOR
                _slide = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(SlideMaterialPath);
                if (_slide != null)
                    return _slide;
#endif

                _slide = new PhysicsMaterial("SlideRuntime")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
                return _slide;
            }
        }
    }
}
