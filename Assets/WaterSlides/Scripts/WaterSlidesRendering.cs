using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSlides
{
    public static class WaterSlidesRendering
    {
        public static bool UsesBuiltInPipeline => GraphicsSettings.currentRenderPipeline == null;

        public static Shader GetSlideShader()
        {
            if (UsesBuiltInPipeline)
                return Shader.Find("Standard");

            var urp = Shader.Find("Universal Render Pipeline/Lit");
            return urp != null ? urp : Shader.Find("Standard");
        }

        public static void ApplySlideColor(Material mat, Color color)
        {
            if (mat == null)
                return;

            color.a = 1f;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;
        }

        public static void ConfigureSlideMaterial(Material mat)
        {
            if (mat == null)
                return;

            if (UsesBuiltInPipeline)
            {
                mat.SetFloat("_Mode", 0f);
                mat.SetInt("_SrcBlend", (int)BlendMode.One);
                mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = -1;
                mat.SetFloat("_Glossiness", 0.92f);
                mat.SetFloat("_Metallic", 0.02f);
            }
            else
            {
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_Smoothness", 0.92f);
                mat.SetFloat("_Metallic", 0.02f);
            }
        }
    }
}
