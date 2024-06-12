namespace RuneLover.Casts.Mono;

public class CastPrefabMono : MonoBehaviour
{
    private List<MeshRenderer> _meshes;
    private List<Light> _lights;
    private List<ParticleSystemRenderer> _particles;
    private static readonly int EmissiveColorShaderID = Shader.PropertyToID("_EmissionColor");

    public VisualSettings VisualSettings;

    public void InitReferences()
    {
        Debug("CastPrefabMono.InitReferences 0");
        //TODO: cash some references in CastPrefabMono.InitReferences
        _meshes = GetComponentsInChildren<MeshRenderer>(true).ToList();
        _lights = GetComponentsInChildren<Light>(true).ToList();
        _particles = GetComponentsInChildren<ParticleSystemRenderer>(true).ToList();

        Debug(
            $"CastPrefabMono.InitReferences 1, meshes: {_meshes?.Count ?? -1}, lights: {_lights?.Count ?? -1}, particles: {_particles?.Count ?? -1}");
    }

    public void ApplyVisualSettings()
    {
        Debug(
            $"CastPrefabMono.ApplyVisualSettings({VisualSettings?.ToString() ?? "null"}), meshes: {_meshes?.Count ?? -1}, lights: {_lights?.Count ?? -1}, particles: {_particles?.Count ?? -1}");
        foreach (var mesh in _meshes)
        {
            if (mesh.name.EndsWith("_main"))
            {
                mesh.material.SetColor("_EmissionColor", VisualSettings.MainColor);
                Debug(
                    $"Set color of {mesh.name} to MainColor={VisualSettings.MainColor} -> {mesh.material.GetColor("_EmissionColor")}");
            } 
            else if (mesh.name.EndsWith("_secondary"))
            {
                mesh.material.SetColor("_EmissionColor", VisualSettings.SecondaryColor);
                Debug(
                    $"Set color of {mesh.name} to SecondaryColor={VisualSettings.SecondaryColor} -> {mesh.material.GetColor("_EmissionColor")}");
            }
        }

        foreach (var particle in _particles)
        {
            if (particle.name.EndsWith("_main"))
            {
                particle.material.color = VisualSettings.ParticleColor;
                Debug($"Set color of {particle.name} to ParticleColor={VisualSettings.ParticleColor}");
            }
            else if (particle.name.EndsWith("_secondary"))
            {
                particle.material.color = VisualSettings.ParticleColor2;
                Debug($"Set color of {particle.name} to ParticleColor2={VisualSettings.ParticleColor2}");
            }
        }

        foreach (var light in _lights)
        {
            light.color = VisualSettings.LightColor;
            Debug($"Set color of {light.name} to LightColor={VisualSettings.LightColor}");
        }

        foreach (var scale in transform.FindChildsByName("ApplyScale"))
        {
            scale.transform.localScale = VisualSettings.Scale;
            Debug($"Set scale of ApplyScale to Scale={VisualSettings.Scale}");
        }
    }
}