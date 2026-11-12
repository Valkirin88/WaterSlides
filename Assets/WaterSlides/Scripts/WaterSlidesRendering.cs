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

        // Пластик непрозрачных горок (свечение + износ). Только Built-in RP; лежит в
        // Resources, поэтому есть и в сборке. null — использовать GetSlideShader.
        public static Shader GetPlasticShader()
        {
            if (!UsesBuiltInPipeline)
                return null;
            var shader = Shader.Find("WaterSlides/SlidePlastic");
            return shader != null && shader.isSupported ? shader : null;
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
                mat.DisableKeyword("_EMISSION");
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

        // «Стекло» закрытой трубы: полупрозрачный Standard (режим Fade) с эмиссией
        // для колец подсветки. Альфа берётся из текстуры × цвета материала.
        public static void ConfigureGlassMaterial(Material mat)
        {
            if (mat == null)
                return;

            var unlit = Shader.Find("Unlit/Transparent");
            if (unlit != null)
            {
                // Unlit/Transparent: цвет и альфа — целиком из текстуры (ApplyGlassTube
                // печёт тон стекла и неон в неё). Standard Fade здесь не годится —
                // см. комментарий в WaterSlideGenerator.ApplyGlassTube.
                if (mat.shader != unlit)
                    mat.shader = unlit;
                mat.renderQueue = (int)RenderQueue.Transparent - 1;
                return;
            }

            if (UsesBuiltInPipeline)
            {
                mat.SetFloat("_Mode", 2f);
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                // Чуть раньше ленты воды (Transparent): у открытого стеклянного жёлоба
                // вода лежит на стекле и должна рисоваться поверх него, а её GrabPass —
                // видеть стекло и землю за ним.
                mat.renderQueue = (int)RenderQueue.Transparent - 1;
                // Без зеркальных отражений и бликов: с глянцем 0.96 Fresnel под
                // скользящим углом отражал светлое небо, и дальняя часть стеклянного
                // жёлоба становилась молочно-белой («туман под определённым углом»).
                // В референсе стекло не отражает ничего — светится только неоном.
                mat.SetFloat("_Glossiness", 0.2f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_SpecularHighlights", 0f);
                mat.SetFloat("_GlossyReflections", 0f);
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            }
            else
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.EnableKeyword("_EMISSION");
                mat.SetFloat("_Smoothness", 0.96f);
                mat.SetFloat("_Metallic", 0.05f);
            }
        }
    }
}
