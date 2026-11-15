// Red Dead Redemption 2's world: each streamed asset's vertices drawn once per placement, as RedFury's vs_world does.
// A placement's world matrix and its inverse transpose stay on the GPU for as long as it's covered; each frame only the
// four-byte indexes of the placements in view move, read per instance. Positions go relative to the scene's origin, the
// camera, so the world's large coordinates stay precise. Grass shrinks away with distance and carries its colour and
// occlusion; the world's local lights (RedFury's, tiled on the screen) light it. Put after scene.wgsl, light.wgsl, rdr2_light.wgsl,
// rdr2_terrain_blending.wgsl, rdr2_material.wgsl and rdr2_vertex.wgsl.

struct WorldInstance {
    m0: vec4f, m1: vec4f, m2: vec4f, m3: vec4f,
    n0: vec4f, n1: vec4f, n2: vec4f, n3: vec4f,
};

// The asset's placements: the world matrix, from the asset's own space, then its inverse transpose. An ordinary
// placement's n3.x carries its tint palette row (0 keeps the material's); a plant's n3 its fade's start and end, 1, and
// its occlusion, and the w of n0 to n2 its colour.
@group(3) @binding(0) var<storage, read> world_instances: array<WorldInstance>;

fn is_grass(instance: WorldInstance) -> bool {
    return instance.n3.z > 0.5;
}

// A plant shrinks to nothing between its fade's start and end, from the camera.
fn grass_fade(instance: WorldInstance) -> f32 {
    if (!is_grass(instance)) {
        return 1.0;
    }
    let distance = length(instance.m3.xyz - scene.origin.xyz);
    return 1.0 - smoothstep(instance.n3.x, max(instance.n3.x + 0.01, instance.n3.y), distance);
}

fn place_instance(input: MaterialVertexInput, instance: WorldInstance) -> MaterialVertexInput {
    let model = mat4x4f(instance.m0, instance.m1, instance.m2, instance.m3);
    let normal_matrix = mat4x4f(instance.n0, instance.n1, instance.n2, vec4f(0.0, 0.0, 0.0, 1.0));
    var placed = input;
    placed.position = (model * vec4f(input.position * grass_fade(instance), 1.0)).xyz;
    placed.normal = (normal_matrix * vec4f(input.normal, 0.0)).xyz;
    let handedness = sign(determinant(mat3x3f(instance.m0.xyz, instance.m1.xyz, instance.m2.xyz)));
    placed.tangent = vec4f((model * vec4f(input.tangent.xyz, 0.0)).xyz, input.tangent.w * handedness);
    return placed;
}

@vertex
fn vs_rdr2_world(vertex: Rdr2Vertex, @location(8) source_index: u32) -> MaterialVertexOutput {
    let instance = world_instances[source_index];
    // In the world, then relative to the scene's origin (the camera), where the material and the view work.
    var placed = place_instance(rdr2_input(vertex), instance);
    placed.position -= scene.origin.xyz;
    if (!is_grass(instance)) {
        instance_tint_row = i32(round(instance.n3.x));
    }
    var output = material_vertex(placed);
    output.relative_position = placed.position;
    if (is_grass(instance)) {
        output.grass_colour_ao = vec4f(instance.n0.w, instance.n1.w, instance.n2.w, instance.n3.w);
    }
    output.position = scene.view_projection * vec4f(placed.position, 1.0);
    return output;
}

// The world's local lights (RedFury's LocalLightFrame): at most 128 nearest the camera that reach the screen, each a
// position relative to the camera and radius, its colour times its intensity and its type (1 a spot), its cone's
// direction and inner cosine, then its outer cosine and falloff exponent; and a 32 by 18 grid of the screen, each
// tile's lights as a 128-bit mask.
struct LocalLight {
    position_radius: vec4f,
    colour_type: vec4f,
    direction_inner: vec4f,
    outer_falloff: vec4f,
};

@group(3) @binding(1) var<storage, read> local_lights: array<LocalLight, 128>;
@group(3) @binding(2) var<storage, read> local_light_tiles: array<vec4u, 576>;

// Unshadowed local direct light (RedFury's, copied). The CPU's conservative screen tiles bound the light loop; exact
// radial and cone rejection happens here, blended surfaces included.
fn local_irradiance(pixel: vec2f, position: vec3f, n: vec3f, v: vec3f, base_colour: vec3f,
    f0: vec3f, roughness: f32, metallic: f32, specular_scale: f32, edge_strength: f32) -> vec3f {
    // The tiles cover the frame the surfaces are drawn into (scene.viewport.zw).
    let tile = min(vec2u(pixel / max(scene.viewport.zw, vec2f(1.0)) * vec2f(32.0, 18.0)), vec2u(31u, 17u));
    let candidates = local_light_tiles[tile.y * 32u + tile.x];
    let ndotv = max(dot(n, v), 0.0001);
    var radiance = vec3f(0.0);
    for (var word = 0u; word < 4u; word++) {
        var mask = candidates[word];
        while (mask != 0u) {
            let index = word * 32u + firstTrailingBit(mask);
            mask &= mask - 1u;
            let light = local_lights[index];
            let delta = light.position_radius.xyz - position;
            let distance = length(delta);
            if (distance >= light.position_radius.w) { continue; }
            let l = delta / max(distance, 0.0001);
            let ndotl = max(dot(n, l), 0.0);
            if (ndotl <= 0.0) { continue; }
            var attenuation = pow(max(1.0 - distance / light.position_radius.w, 0.0), light.outer_falloff.y);
            if (light.colour_type.w > 0.5) {
                let cosine = dot(-l, light.direction_inner.xyz);
                let inner = light.direction_inner.w;
                let outer = light.outer_falloff.x;
                if (cosine < outer) { continue; }
                if (inner > outer) {
                    let cone = clamp((cosine - outer) / (inner - outer), 0.0, 1.0);
                    attenuation *= cone * cone * (3.0 - 2.0 * cone);
                }
            }
            let half_sum = v + l;
            let h = half_sum / max(length(half_sum), 0.0001);
            let fresnel = mix(f0, fresnel_schlick(max(dot(h, v), 0.0), f0), edge_strength);
            let specular = distribution_ggx(n, h, roughness)
                * visibility_smith_schlick(ndotv, ndotl, roughness) * fresnel * specular_scale;
            let diffuse = (1.0 - metallic) * base_colour
                * diffuse_burley(ndotv, ndotl, max(dot(l, h), 0.0), roughness);
            radiance += (diffuse + specular) * light.colour_type.rgb * ndotl * attenuation;
        }
    }
    return radiance;
}

@fragment
fn fs_rdr2_world(input: MaterialVertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    return shade_material(input, front);
}
