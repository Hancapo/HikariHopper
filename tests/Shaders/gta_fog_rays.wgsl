// GTA V's fog along each ray of a buffer, for GtaFogTests: after Gta5/fog.wgsl.

@group(0) @binding(0) var<uniform> fog: GtaFog;
@group(0) @binding(1) var<storage, read> rays: array<vec4f>;
@group(0) @binding(2) var<storage, read_write> fogged: array<vec4f>;

@compute @workgroup_size(1)
fn cs_fog(@builtin(global_invocation_id) id: vec3u) {
    fogged[id.x] = gta_fog(fog, rays[id.x].xyz, 1.0);
}
