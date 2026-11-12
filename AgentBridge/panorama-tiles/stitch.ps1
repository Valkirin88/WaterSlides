Add-Type -AssemblyName System.Drawing
Add-Type -Path 'E:\UnityProjects\WaterSlides\AgentBridge\panorama-tiles\Stitch.cs' -ReferencedAssemblies System.Drawing
[StitchPanorama]::Run('E:\UnityProjects\WaterSlides\AgentBridge\panorama-tiles','E:\UnityProjects\WaterSlides\Assets\WaterSlides\Environment\TropicalLagoon360_Detailed.png')
