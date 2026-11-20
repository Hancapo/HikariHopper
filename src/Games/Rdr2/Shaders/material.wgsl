// Red Dead Redemption 2's material: RedFury.Rendering's viewer_material.wgsl, copied as it is, but for where its light
// comes from. RedFury lights it with its own atmosphere, screen-space shadow mask, GTAO and local lights; here it takes
// the viewer's light (light.wgsl through Rdr2/light.wgsl): the key light with its cascaded shadow, the ambient for a
// normal and the reflection map. What changed from RedFury's file:
//   - group 0's camera and group 2's lighting are the viewer's (scene.wgsl, light.wgsl); RedFury's shadow-caster, world
//     instance, depth-prepass and shadow entry points are left out (the viewer has its own passes);
//   - the vertex comes in the viewer's layout (Rdr2/surface.wgsl's entry points), positions relative to the scene's origin;
//   - the sun's shadow is light.wgsl's cascades for every surface (no screen-space mask), with no screen-space occlusion;
//   - no local lights, no aerial perspective, dry weather, no debug views;
//   - cut-outs in the main view are drawn with the game's sub-sampled alpha (Rdr2/cutout.wgsl);
//   - water is drawn with the generic material, as RedFury draws a water shader it can't: its shading needs a copy of
//     the scene behind it, which the viewer doesn't make yet.
// The rest (layers, terrain blends, tints, dirt, decals, the PBR material decoding and BRDF) is RedFury's, unchanged.

struct MaterialUniforms {
    base_colour_factor: vec4f,
    surface: vec4f,
    emission_alpha_layers: vec4f,
    decal_opacity: vec4f,
    flags: vec4u,
    dirt_settings: vec4f,
    pbr_specular: vec4f,
    layer_normal_scale: vec4f,
    layer_fresnel: vec4f,
    pbr_tweaks: vec4f,
    specular_mask: vec4f,
    layer_tint_strength: vec4f,
    terrain_uv_sets: vec4f,
    terrain_uv_flags: vec4f,
    terrain_uv_scale_bias: vec4f,
    terrain_layer_uv_scale_01: vec4f,
    terrain_layer_uv_scale_23: vec4f,
    terrain_blend_softness: vec4f,
    terrain_hbb_scale: vec4f,
    terrain_hbb_bias: vec4f,
    terrain_pxm_scale: vec4f,
    terrain_pxm_bias: vec4f,
    terrain_tint_intensity: vec4f,
    terrain_tint_blend: vec4f,
    terrain_wetness: vec4f,
    terrain_porosity: vec4f,
    terrain_fresnel: vec4f,
    terrain_height: vec4f,
    decal_height_filter: vec4f,
    decal_height_settings: vec4f,
    water_colour: vec4f,
    water_settings: vec4f,
    water_detail: vec4f,
    water_opacity_override: vec4f,
    grass_settings: vec4f,
};

@group(1) @binding(0) var<uniform> material: MaterialUniforms;
@group(1) @binding(1) var material_sampler: sampler;
@group(1) @binding(2) var base_texture: texture_2d<f32>;
@group(1) @binding(3) var normal_texture: texture_2d<f32>;
@group(1) @binding(4) var occlusion_texture: texture_2d<f32>;
@group(1) @binding(5) var emissive_texture: texture_2d<f32>;
@group(1) @binding(6) var control_texture: texture_2d<f32>;
@group(1) @binding(7) var layer1_texture: texture_2d<f32>;
@group(1) @binding(8) var layer1_normal: texture_2d<f32>;
@group(1) @binding(9) var layer2_texture: texture_2d<f32>;
@group(1) @binding(10) var layer2_normal: texture_2d<f32>;
@group(1) @binding(11) var layer3_texture: texture_2d<f32>;
@group(1) @binding(12) var layer3_normal: texture_2d<f32>;
@group(1) @binding(13) var dirt_texture: texture_2d<f32>;
@group(1) @binding(14) var tint_palette_texture: texture_2d<f32>;
@group(1) @binding(15) var material0_texture: texture_2d<f32>;
@group(1) @binding(16) var material1_texture: texture_2d<f32>;
@group(1) @binding(17) var material2_texture: texture_2d<f32>;
@group(1) @binding(18) var material3_texture: texture_2d<f32>;
@group(1) @binding(19) var terrain_tint_texture: texture_2d<f32>;
@group(1) @binding(20) var terrain_tint_normal: texture_2d<f32>;
@group(1) @binding(21) var terrain_height_texture: texture_2d<f32>;
@group(1) @binding(22) var material_b0_texture: texture_2d<f32>;
@group(1) @binding(23) var material_b1_texture: texture_2d<f32>;
@group(1) @binding(24) var material_b2_texture: texture_2d<f32>;
@group(1) @binding(25) var material_b3_texture: texture_2d<f32>;
@group(1) @binding(26) var clamp_sampler: sampler;
// Vertex coordinates and encoded BC/raw rows both retain their authored orientation.
// No image or UV conversion is required at this boundary.
fn sample_rage(image: texture_2d<f32>, filtering: sampler, uv: vec2f) -> vec4f {
    return textureSample(image, filtering, uv);
}

const PI: f32 = 3.14159265359;

struct MaterialVertexInput {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec4f,
    @location(3) uv: vec2f,
    @location(4) uv1: vec2f,
    @location(5) uv2: vec2f,
    @location(6) colour: vec4f,
    @location(7) tint_palette_coordinate: f32,
};

struct MaterialVertexOutput {
    // Invariant: the prepass and the forward pass must produce bit-identical depth.
    @builtin(position) @invariant position: vec4f,
    @location(0) world_position: vec3f,
    @location(1) uv: vec2f,
    @location(2) colour: vec4f,
    @location(3) tangent: vec3f,
    @location(4) bitangent: vec3f,
    @location(5) normal: vec3f,
    @location(6) palette_tint: vec3f,
    @location(7) uv1: vec2f,
    @location(8) uv2: vec2f,
    @location(9) grass_colour_ao: vec4f,
    @location(10) relative_position: vec3f,
    @location(11) hemisphere: f32,
    // Its placement's level of detail fade in a world (light.wgsl's lod_fade_keeps): none in the viewer.
    @location(12) @interpolate(flat) lod_fade: vec2f,
};

fn material_vertex_geometry(input: MaterialVertexInput) -> MaterialVertexOutput {
    var output: MaterialVertexOutput;
    let n = normalize(input.normal);
    var projected_tangent = input.tangent.xyz - n * dot(n, input.tangent.xyz);
    if (dot(projected_tangent, projected_tangent) < 0.00000001) {
        var fallback_axis = vec3f(1.0, 0.0, 0.0);
        if (abs(n.x) > 0.9) {
            fallback_axis = vec3f(0.0, 1.0, 0.0);
        }
        projected_tangent = cross(fallback_axis, n);
    }
    let tangent = normalize(projected_tangent);
    output.world_position = input.position;
    output.uv = input.uv;
    output.uv1 = input.uv1;
    output.uv2 = input.uv2;
    output.colour = input.colour;
    output.tangent = tangent;
    // Retail vertex programs build the binormal as cross(tangent, normal) * w; the opposite order
    // inverts the normal map's green channel and lights bumps from the wrong side along V.
    output.bitangent = normalize(cross(tangent, n)) * input.tangent.w;
    output.normal = n;
    output.palette_tint = vec3f(1.0);
    output.grass_colour_ao = vec4f(1.0);
    output.hemisphere = 1.0;
    return output;
}

fn material_vertex(input: MaterialVertexInput) -> MaterialVertexOutput {
    var output = material_vertex_geometry(input);
    if ((material.flags.w & 64u) != 0u) {
        // Retail samples COLOR0.B with PointClamp at mip zero in the vertex stage.
        // Interpolate colours, never palette indices: intermediate slots may be unrelated colours.
        output.palette_tint = vertex_tint_palette(input.tint_palette_coordinate);
    }
    return output;
}

fn distribution_ggx(n: vec3f, h: vec3f, roughness: f32) -> f32 {
    let alpha = roughness * roughness;
    let alpha2 = alpha * alpha;
    let ndoth = max(dot(n, h), 0.0);
    let denominator = ndoth * ndoth * (alpha2 - 1.0) + 1.0;
    return alpha2 / max(PI * denominator * denominator, 0.000001);
}

// Analytic fit of the split-sum environment BRDF (Karis 2014): returns scale and bias for F0.
fn environment_brdf(ndotv: f32, roughness: f32) -> vec2f {
    let c0 = vec4f(-1.0, -0.0275, -0.572, 0.022);
    let c1 = vec4f(1.0, 0.0425, 1.04, -0.04);
    let r = roughness * c0 + c1;
    let a004 = min(r.x * r.x, exp2(-9.28 * ndotv)) * r.x + r.y;
    return vec2f(-1.04, 1.04) * a004 + r.zw;
}

// Multi-bounce approximation for ambient occlusion (Jimenez et al. 2016): brighter albedo
// recovers light that a single-bounce visibility term would lose.
fn multi_bounce_occlusion(visibility: f32, albedo: vec3f) -> vec3f {
    let a = 2.0404 * albedo - 0.3324;
    let b = -4.7951 * albedo + 0.6417;
    let c = 2.7552 * albedo + 0.6903;
    return max(vec3f(visibility), ((visibility * a + b) * visibility + c) * visibility);
}

// Specular occlusion from ambient visibility (Lagarde and de Rousiers 2014).
fn specular_occlusion(ndotv: f32, visibility: f32, roughness: f32) -> f32 {
    return clamp(pow(ndotv + visibility, exp2(-16.0 * roughness - 1.0)) - 1.0 + visibility, 0.0, 1.0);
}

// The retail lighting program limits the grazing reflectance to saturate(50 * mean(f0)): surfaces
// with almost no specular reflectance (dry soil, grass) get no white Fresnel sheen at grazing angles.
fn fresnel_f90(f0: vec3f) -> f32 {
    return clamp(dot(f0, vec3f(0.33)) * 50.0, 0.0, 1.0);
}

fn fresnel_schlick(cos_theta: f32, f0: vec3f) -> vec3f {
    return f0 + (vec3f(fresnel_f90(f0)) - f0)
        * pow(clamp(1.0 - cos_theta, 0.0, 1.0), 5.0);
}

// Retail Smith-Schlick visibility with k = alpha / 2, returned as G / (4 NdotL NdotV).
fn visibility_smith_schlick(ndotv: f32, ndotl: f32, roughness: f32) -> f32 {
    let k = max(roughness * roughness, 0.0002) * 0.5;
    let view = ndotv / (ndotv * (1.0 - k) + k);
    let light = ndotl / (ndotl * (1.0 - k) + k);
    return view * light / max(4.0 * ndotv * ndotl, 0.0001);
}

// Retail diffuse: Frostbite's energy-normalized Disney (Burley) term, divided by pi.
fn diffuse_burley(ndotv: f32, ndotl: f32, ldoth: f32, roughness: f32) -> f32 {
    let energy_bias = 0.5 * roughness;
    let energy_factor = mix(1.0, 1.0 / 1.52, roughness);
    let f90 = energy_bias + 2.0 * ldoth * ldoth * roughness;
    let light = 1.0 + (f90 - 1.0) * pow(1.0 - ndotl, 5.0);
    let view = 1.0 + (f90 - 1.0) * pow(1.0 - ndotv, 5.0);
    return light * view * energy_factor / PI;
}

// Unshadowed local direct light (RedFury's, copied): light.wgsl's local lights, for a model in the viewer and the
// world alike. The CPU's conservative screen tiles bound the light loop; exact radial and cone rejection happens here,
// blended surfaces included.
fn local_irradiance(pixel: vec2f, position: vec3f, n: vec3f, v: vec3f, base_colour: vec3f,
    f0: vec3f, roughness: f32, metallic: f32, specular_scale: f32, edge_strength: f32) -> vec3f {
    let tile = local_light_tile();
    let ndotv = max(dot(n, v), 0.0001);
    var radiance = vec3f(0.0);
    for (var word = 0u; word < 16u; word++) {
        var mask = local_light_word(tile, word);
        while (mask != 0u) {
            let index = word * 32u + firstTrailingBit(mask);
            mask &= mask - 1u;
            let light = local_lights[index];
            let delta = local_light_point(light) - position;
            let distance = length(delta);
            if (distance >= light.position_radius.w) { continue; }
            let l = delta / max(distance, 0.0001);
            let ndotl = max(dot(n, l), 0.0);
            if (ndotl <= 0.0) { continue; }
            var attenuation = pow(max(1.0 - distance / light.position_radius.w, 0.0), light.outer_falloff.y);
            if (light.colour_type.w > 0.5 && light.colour_type.w < 1.5) {
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

fn terrain_flags() -> u32 {
    return u32(material.terrain_uv_flags.y + 0.5);
}

fn native_terrain_blend() -> bool { return (terrain_flags() & 1024u) != 0u; }
// Retail terrain_lod_* / terrain_slod deferred programs: the bump texture is the world-space normal (no tangent
// frame, no bumpiness), SpecularTexture.G is smoothness, .R scales f0 and metalness is zero.
fn lod_terrain_encoding() -> bool { return (terrain_flags() & 16384u) != 0u; }

fn select_texcoord(input: MaterialVertexOutput, selector: f32) -> vec2f {
    let index = u32(clamp(round(selector), 0.0, 2.0));
    if (index == 1u) {
        return input.uv1;
    }
    if (index == 2u) {
        return input.uv2;
    }
    return input.uv;
}

fn terrain_lookup_uv(input: MaterialVertexOutput) -> vec2f {
    let transformed = select_texcoord(input, material.terrain_uv_sets.x);
    return select(
        transformed,
        clamp(transformed, vec2f(0.0), vec2f(1.0)),
        (terrain_flags() & 2u) != 0u,
    );
}

fn layer_uv(input: MaterialVertexOutput, index: u32) -> vec2f {
    if ((terrain_flags() & 1u) == 0u) {
        return input.uv;
    }
    var selector = material.terrain_uv_sets.y;
    var scale = material.terrain_layer_uv_scale_01.xy;
    if (index == 1u) {
        selector = material.terrain_uv_sets.z;
        scale = material.terrain_layer_uv_scale_01.zw;
    } else if (index == 2u) {
        selector = material.terrain_uv_sets.w;
        scale = material.terrain_layer_uv_scale_23.xy;
    } else if (index == 3u) {
        selector = material.terrain_uv_flags.x;
        scale = material.terrain_layer_uv_scale_23.zw;
    }
    var uv = select_texcoord(input, selector);
    let selector_index = u32(clamp(round(selector), 0.0, 2.0));
    // Terrain UV0 is the normalized tile coordinate and needs UVScaleBias to
    // reach authored world tiling. UV1/UV2 are already pre-tiled by the vertex
    // stream; multiplying those by values such as 128 creates severe moire.
    if (selector_index == 0u) {
        uv = uv * material.terrain_uv_scale_bias.xy
            + material.terrain_uv_scale_bias.zw;
    }
    return uv * scale;
}

fn terrain_material_height(input: MaterialVertexOutput, index: u32) -> f32 {
    let uv = layer_uv(input, index);
    var value = sample_rage(material0_texture, material_sampler, uv).a;
    if (index == 1u) {
        value = sample_rage(material1_texture, material_sampler, uv).a;
    } else if (index == 2u) {
        value = sample_rage(material2_texture, material_sampler, uv).a;
    } else if (index == 3u) {
        value = sample_rage(material3_texture, material_sampler, uv).a;
    }
    if ((terrain_flags() & 512u) != 0u) {
        let displacement = sample_rage(
            terrain_height_texture,
            material_sampler,
            terrain_lookup_uv(input),
        ).r * material.terrain_height.z + material.terrain_height.w;
        value = mix(value, displacement, 0.5);
    }
    return value;
}

fn terrain_material_a(input: MaterialVertexOutput, index: u32) -> vec4f {
    let uv = layer_uv(input, index);
    if (index == 1u) { return sample_rage(material1_texture, material_sampler, uv); }
    if (index == 2u) { return sample_rage(material2_texture, material_sampler, uv); }
    if (index == 3u) { return sample_rage(material3_texture, material_sampler, uv); }
    return sample_rage(material0_texture, material_sampler, uv);
}

fn layer_weights(input: MaterialVertexOutput) -> vec4f {
    let layer_count = u32(material.emission_alpha_layers.z + 0.5);
    let has_control = material.flags.w & 1u;
    if (layer_count <= 1u) {
        return vec4f(1.0, 0.0, 0.0, 0.0);
    }
    if ((terrain_flags() & 8192u) != 0u) { return terrain_lod_weights(input.colour); }
    if (native_terrain_blend()) {
        let uv = select_texcoord(input, material.terrain_uv_sets.x);
        var lookup = sample_rage(control_texture, material_sampler, uv).rgb;
        if ((terrain_flags() & 2u) != 0u) {
            lookup = sample_rage(control_texture, clamp_sampler, uv).rgb;
        }
        let two_layers = (terrain_flags() & 4096u) != 0u;
        let mask = terrain_painted_weights(lookup, input.colour, two_layers);
        if ((terrain_flags() & 2048u) != 0u) { return mask; }
        var heights = vec4f(terrain_material_a(input, 0u).b, terrain_material_a(input, 1u).b,
            terrain_material_a(input, 2u).b, terrain_material_a(input, 3u).b);
        if (two_layers) { heights.z = 0.0; heights.w = 0.0; }
        return terrain_height_weights(mask, heights,
            material.terrain_hbb_scale, material.terrain_hbb_bias, material.terrain_blend_softness);
    }
    if (has_control == 0u) {
        if ((terrain_flags() & 1u) != 0u && layer_count == 2u) {
            let lod_blend = clamp(input.colour.b, 0.0, 1.0);
            return vec4f(1.0 - lod_blend, lod_blend, 0.0, 0.0);
        }
        return vec4f(1.0, 0.0, 0.0, 0.0);
    }
    var control = max(
        sample_rage(control_texture, material_sampler, terrain_lookup_uv(input)),
        vec4f(0.0),
    );
    if (layer_count == 2u) {
        control = vec4f(1.0 - control.r, control.r, 0.0, 0.0);
    } else if (layer_count == 3u) {
        control = vec4f(control.rgb, 0.0);
    }
    if ((terrain_flags() & 1u) != 0u) {
        let softness = max(material.terrain_blend_softness, vec4f(0.05));
        control = pow(max(control, vec4f(0.0001)), softness);
        let raw_height = vec4f(
            terrain_material_height(input, 0u),
            terrain_material_height(input, 1u),
            terrain_material_height(input, 2u),
            terrain_material_height(input, 3u),
        );
        if ((terrain_flags() & 8u) != 0u) {
            let pxm = clamp(
                raw_height * material.terrain_pxm_scale
                    + material.terrain_pxm_bias,
                vec4f(0.0),
                vec4f(1.0),
            );
            control *= mix(vec4f(1.0), max(pxm, vec4f(0.05)), 0.65);
        }
        if ((terrain_flags() & 4u) != 0u) {
            let hbb = raw_height * material.terrain_hbb_scale
                + material.terrain_hbb_bias;
            let peak = max(max(hbb.x, hbb.y), max(hbb.z, hbb.w));
            let gate = smoothstep(
                vec4f(peak) - 0.25 / softness,
                vec4f(peak) + 0.02,
                hbb,
            );
            control *= max(gate, vec4f(0.025));
        }
    }
    let total = dot(control, vec4f(1.0));
    if (total > 0.0001) {
        return control / total;
    }
    return vec4f(1.0, 0.0, 0.0, 0.0);
}

fn tinted_colour(
    texture_value: vec4f,
    palette_tint: vec3f,
    tint_strength: f32,
) -> vec4f {
    let tint = mix(
        vec3f(1.0),
        palette_tint,
        clamp(tint_strength, 0.0, 1.0),
    );
    return vec4f(texture_value.rgb * tint, texture_value.a);
}

fn apply_terrain_tint(
    input: MaterialVertexOutput,
    colour: vec4f,
    weights: vec4f,
) -> vec4f {
    if (native_terrain_blend() || (terrain_flags() & 128u) == 0u) {
        return colour;
    }
    let tint = sample_rage(
        terrain_tint_texture,
        material_sampler,
        terrain_lookup_uv(input),
    ).rgb;
    let blend = clamp(dot(weights, material.terrain_tint_blend), 0.0, 1.0);
    // Retail intensity spans a much wider domain than a colour multiplier.
    // Normalize it before applying the authored blend so strong values do not
    // crush the albedo to black.
    let intensity = max(dot(weights, material.terrain_tint_intensity), 0.0);
    let strength = blend * intensity / (intensity + 16.0);
    let tint_multiplier = clamp(
        tint * 2.0,
        vec3f(0.35),
        vec3f(1.65),
    );
    return vec4f(
        colour.rgb * mix(vec3f(1.0), tint_multiplier, strength),
        colour.a,
    );
}

fn sample_colour(
    input: MaterialVertexOutput,
    weights: vec4f,
    palette_tint: vec3f,
) -> vec4f {
    if (native_terrain_blend()) {
        // Macro colour uses UV0, independently of the blend lookup's selector.
        let macro_colour = sample_rage(terrain_tint_texture, clamp_sampler, input.uv).rgb;
        var colours = array<vec4f, 4>(
            sample_rage(base_texture, material_sampler, layer_uv(input, 0u)),
            sample_rage(layer1_texture, material_sampler, layer_uv(input, 1u)),
            sample_rage(layer2_texture, material_sampler, layer_uv(input, 2u)),
            sample_rage(layer3_texture, material_sampler, layer_uv(input, 3u)));
        let masks = vec4f(terrain_material_a(input, 0u).a, terrain_material_a(input, 1u).a,
            terrain_material_a(input, 2u).a, terrain_material_a(input, 3u).a);
        var sum = vec4f(0.0);
        for (var i = 0u; i < 4u; i++) {
            var rgb = colours[i].rgb;
            if ((terrain_flags() & 128u) != 0u) {
                rgb = terrain_layer_tint(rgb, macro_colour, masks[i], material.terrain_tint_intensity[i], material.terrain_tint_blend[i]);
            }
            sum += vec4f(rgb, colours[i].a) * weights[i];
        }
        return sum;
    }
    let layer_count = u32(material.emission_alpha_layers.z + 0.5);
    var colour = tinted_colour(
        sample_rage(base_texture, material_sampler, layer_uv(input, 0u)),
        palette_tint,
        material.layer_tint_strength.x,
    ) * weights.x;
    if (layer_count > 1u) {
        colour += tinted_colour(
            sample_rage(layer1_texture, material_sampler, layer_uv(input, 1u)),
            palette_tint,
            material.layer_tint_strength.y,
        ) * weights.y;
    }
    if (layer_count > 2u) {
        colour += tinted_colour(
            sample_rage(layer2_texture, material_sampler, layer_uv(input, 2u)),
            palette_tint,
            material.layer_tint_strength.z,
        ) * weights.z;
    }
    if (layer_count > 3u) {
        colour += tinted_colour(
            sample_rage(layer3_texture, material_sampler, layer_uv(input, 3u)),
            palette_tint,
            material.layer_tint_strength.w,
        ) * weights.w;
    }
    return colour;
}

fn sample_material_packed(input: MaterialVertexOutput, weights: vec4f) -> vec3f {
    let layer_count = u32(material.emission_alpha_layers.z + 0.5);
    let material_mask = material.flags.y >> 1u;
    let fallback = vec3f(
        material.surface.y,
        material.surface.x,
        material.surface.z,
    );
    if (material_mask == 0u) {
        return fallback;
    }
    var packed = select(
        fallback,
        sample_rage(
            material0_texture, material_sampler, layer_uv(input, 0u)
        ).rgb,
        (material_mask & 1u) != 0u,
    ) * weights.x;
    if (layer_count > 1u) {
        packed += select(
            fallback,
            sample_rage(
                material1_texture, material_sampler, layer_uv(input, 1u)
            ).rgb,
            (material_mask & 2u) != 0u,
        ) * weights.y;
    }
    if (layer_count > 2u) {
        packed += select(
            fallback,
            sample_rage(
                material2_texture, material_sampler, layer_uv(input, 2u)
            ).rgb,
            (material_mask & 4u) != 0u,
        ) * weights.z;
    }
    if (layer_count > 3u) {
        packed += select(
            fallback,
            sample_rage(
                material3_texture, material_sampler, layer_uv(input, 3u)
            ).rgb,
            (material_mask & 8u) != 0u,
        ) * weights.w;
    }
    return packed;
}

fn sample_terrain_material_b(
    input: MaterialVertexOutput,
    weights: vec4f,
) -> vec4f {
    let layer_count = u32(material.emission_alpha_layers.z + 0.5);
    var packed = sample_rage(
        material_b0_texture,
        material_sampler,
        layer_uv(input, 0u),
    ) * weights.x;
    if (layer_count > 1u) {
        packed += sample_rage(
            material_b1_texture,
            material_sampler,
            layer_uv(input, 1u),
        ) * weights.y;
    }
    if (layer_count > 2u) {
        packed += sample_rage(
            material_b2_texture,
            material_sampler,
            layer_uv(input, 2u),
        ) * weights.z;
    }
    if (layer_count > 3u) {
        packed += sample_rage(
            material_b3_texture,
            material_sampler,
            layer_uv(input, 3u),
        ) * weights.w;
    }
    return packed;
}

fn decoded_normal(value: vec3f, normal_scale: f32, layer: u32) -> vec3f {
    if (native_terrain_blend()) { return terrain_normal_rg(value.rg, normal_scale); }
    var normal = value * 2.0 - 1.0;
    if ((material.flags.x & (1u << (8u + layer))) != 0u) {
        normal.z = sqrt(max(0.0, 1.0 - dot(normal.xy, normal.xy)));
    }
    let scale = select(normal_scale, 1.0, lod_terrain_encoding());
    return vec3f(
        normal.x * scale,
        normal.y * scale,
        normal.z,
    );
}

fn sample_normal(
    input: MaterialVertexOutput,
    weights: vec4f,
) -> vec3f {
    let layer_count = u32(material.emission_alpha_layers.z + 0.5);
    let normal_mask = material.flags.x;
    if (normal_mask == 0u) {
        return normalize(input.normal);
    }
    var value = vec3f(0.0, 0.0, 1.0);
    if ((normal_mask & 1u) != 0u) {
        value = decoded_normal(
            sample_rage(
                normal_texture, material_sampler, layer_uv(input, 0u)
            ).xyz,
            material.layer_normal_scale.x, 0u,
        );
    }
    var blended = value * weights.x;
    if (layer_count > 1u && (normal_mask & 2u) != 0u) {
        blended += decoded_normal(
            sample_rage(
                layer1_normal, material_sampler, layer_uv(input, 1u)
            ).xyz,
            material.layer_normal_scale.y, 1u,
        ) * weights.y;
    }
    if (layer_count > 2u && (normal_mask & 4u) != 0u) {
        blended += decoded_normal(
            sample_rage(
                layer2_normal, material_sampler, layer_uv(input, 2u)
            ).xyz,
            material.layer_normal_scale.z, 2u,
        ) * weights.z;
    }
    if (layer_count > 3u && (normal_mask & 8u) != 0u) {
        blended += decoded_normal(
            sample_rage(
                layer3_normal, material_sampler, layer_uv(input, 3u)
            ).xyz,
            material.layer_normal_scale.w, 3u,
        ) * weights.w;
    }
    if ((terrain_flags() & 256u) != 0u && native_terrain_blend()) {
        let tint_rg = sample_rage(terrain_tint_normal, clamp_sampler, input.uv).rg;
        blended = terrain_reorient_normal(blended, tint_rg);
    } else if ((terrain_flags() & 256u) != 0u) {
        let tint_normal_value = decoded_normal(
            sample_rage(
                terrain_tint_normal,
                material_sampler,
                terrain_lookup_uv(input),
            ).xyz,
            1.0, 4u,
        );
        let tint_normal_strength = clamp(
            dot(weights, material.terrain_tint_blend) * 0.25,
            0.0,
            0.25,
        );
        blended = normalize(mix(blended, tint_normal_value, tint_normal_strength));
    }
    if (lod_terrain_encoding()) {
        return normalize(blended);
    }
    let tangent_frame = mat3x3<f32>(
        input.tangent,
        input.bitangent,
        input.normal,
    );
    return normalize(tangent_frame * normalize(blended));
}

fn decal_irradiance(n: vec3f) -> vec3f {
    return sky_irradiance(n) + rdr2_sun()
        * max(dot(n, light.sun_direction.xyz), 0.0);
}

// Entity tint palette row of the placement being drawn; 0 keeps the material's row (set by vs_world).
var<private> instance_tint_row: i32 = 0;

fn vertex_tint_palette(coordinate: f32) -> vec3f {
    let size = textureDimensions(tint_palette_texture);
    // Normalized point sampling selects floor(uv * size), clamped at the edges.
    let x = min(i32(floor(clamp(coordinate, 0.0, 1.0) * f32(size.x))), i32(size.x) - 1);
    var source_row = i32(floor(clamp(material.dirt_settings.y, 0.0, 1.0) * f32(size.y)));
    if (instance_tint_row > 0) {
        source_row = instance_tint_row;
    }
    let viewer_row = -1;
    if (viewer_row >= 0) {
        source_row = viewer_row;
    }
    source_row = clamp(source_row, 0, i32(max(size.y, 1u)) - 1);
    // Direct encoded uploads retain authored palette row order.
    let y = source_row;
    return textureLoad(tint_palette_texture, vec2i(x, y), 0).rgb;
}

fn sample_tint_palette(input: MaterialVertexOutput) -> vec3f {
    return input.palette_tint;
}

struct MaterialAlbedo {
    weights: vec4f,
    sampled_colour: vec4f,
    dirt_mask: f32,
    albedo: vec4f,
};

// Layer blend, tint, vertex colour and dirt. The caller supplies the palette tint so alpha-only
// passes (prepass, shadow casters) never reference scene uniforms they do not bind.
fn material_albedo(input: MaterialVertexOutput, palette_tint: vec3f) -> MaterialAlbedo {
    let weights = layer_weights(input);
    let vertex_colour_mode = (material.flags.w >> 3u) & 3u;
    let has_dirt = (material.flags.w & 32u) != 0u;
    var sampled_colour = sample_colour(input, weights, palette_tint);
    sampled_colour = apply_terrain_tint(input, sampled_colour, weights);
    if (material.grass_settings.w > 0.5) {
        sampled_colour = vec4f(sampled_colour.rgb * mix(vec3f(1.0), input.grass_colour_ao.rgb, material.grass_settings.x)
            * mix(0.35, 1.0, clamp(input.grass_colour_ao.a, 0.0, 1.0)), sampled_colour.a);
    }
    var vertex_tint = vec3f(1.0);
    var vertex_coverage = 1.0;
    var dirt_mask = 0.0;
    if (vertex_colour_mode == 1u) {
        vertex_tint = input.colour.rgb;
    } else if (vertex_colour_mode == 2u) {
        vertex_coverage = input.colour.a;
    } else if (vertex_colour_mode == 3u && has_dirt) {
        // Retail dirt programs sample DiffuseTex2 with UV1 and weight it by its alpha, vertex alpha and dirtFactor.
        let dirt = sample_rage(dirt_texture, material_sampler, input.uv1);
        dirt_mask = clamp(dirt.a * input.colour.a * material.dirt_settings.x, 0.0, 1.0);
        let base = sampled_colour.rgb;
        var dirty = dirt.rgb;
        if (material.dirt_settings.w > 0.5) {
            // Hard light over the base, first desaturated by the same weight.
            let grey = mix(base, vec3f(dot(base, vec3f(0.3, 0.59, 0.11))), dirt_mask);
            dirty = select(vec3f(1.0) - 2.0 * (vec3f(1.0) - grey) * (vec3f(1.0) - dirt.rgb), 2.0 * dirt.rgb * grey, dirt.rgb < vec3f(0.5));
        }
        sampled_colour = vec4f(mix(base, dirty, dirt_mask), sampled_colour.a);
    }
    let albedo = vec4f(
        sampled_colour.rgb * material.base_colour_factor.rgb * vertex_tint,
        sampled_colour.a * material.base_colour_factor.a * vertex_coverage,
    );
    return MaterialAlbedo(weights, sampled_colour, dirt_mask, albedo);
}

fn alpha_tested_out(input: MaterialVertexOutput) -> bool {
    if (u32(material.emission_alpha_layers.w + 0.5) != 1u) {
        return false;
    }
    return material_albedo(input, vec3f(1.0)).albedo.a < material.emission_alpha_layers.y;
}

// Whether what's shaded goes to the main view, where cut-outs are sub-sampled: set by the view's entry points.
var<private> sub_sampled_view: bool = false;

fn shade_material(
    input: MaterialVertexOutput,
    front_facing: bool,
) -> vec4f {
    let has_tint_palette = (material.flags.w & 64u) != 0u;
    var palette_tint = vec3f(1.0);
    if (has_tint_palette) {
        palette_tint = sample_tint_palette(input);
    }
    let surface = material_albedo(input, palette_tint);
    let weights = surface.weights;
    let sampled_colour = surface.sampled_colour;
    let dirt_mask = surface.dirt_mask;
    var albedo = surface.albedo;
    let alpha_mode = u32(material.emission_alpha_layers.w + 0.5);
    let is_decal = (material.flags.w & 2u) != 0u;
    var final_alpha = select(
        albedo.a,
        albedo.a * material.decal_opacity.x,
        is_decal,
    );
    if (material.decal_height_settings.x != 0.0) {
        let uv = select_texcoord(input, material.decal_height_settings.z);
        let height = dot(sample_rage(terrain_height_texture, material_sampler, uv), material.decal_height_filter);
        final_alpha = decal_hbb_coverage(height, input.colour.a, material.decal_height_settings.y,
            material.decal_height_settings.w != 0.0) * material.decal_opacity.x;
    }
    let packed_material = sample_material_packed(input, weights);
    let terrain_material_b = sample_terrain_material_b(input, weights);
    let material_map_mask = material.flags.y >> 1u;
    let legacy_specular = material.pbr_specular.y > 0.0001;
    // Shader-specific conservative decoding, independent of authored specular power.
    let specular_only = (material.flags.w & 128u) != 0u;
    var surface_roughness = material.surface.x;
    var surface_metallic = material.surface.y;
    var surface_ao = material.surface.z;
    var specular_texture_intensity = 1.0;
    if (material_map_mask != 0u) {
        specular_texture_intensity = clamp(
            dot(packed_material, material.specular_mask.xyz),
            0.0,
            1.0,
        );
        if (native_terrain_blend()) {
            surface_metallic = 0.0;
            // The deferred terrain program writes GBuffer smoothness = 1 - sum(weight * MaterialA.G) and the
            // lighting program reads roughness = 1 - smoothness, so Material A green is roughness itself.
            surface_roughness = packed_material.g;
            surface_ao *= packed_material.r;
            specular_texture_intensity = 1.0;
        } else if (lod_terrain_encoding()) {
            // LOD terrain writes GBuffer smoothness = SpecularTexture.G directly, so roughness is its complement.
            surface_metallic = 0.0;
            surface_roughness = 1.0 - packed_material.g;
            specular_texture_intensity = 1.0;
        } else if (!legacy_specular && !specular_only) {
            surface_metallic = packed_material.r;
            surface_roughness = packed_material.g;
            surface_ao *= packed_material.b;
        }
    }
    var terrain_wetness = 0.0;
    var wetness_response = 0.0;
    if ((terrain_flags() & 1u) != 0u) {
        var authored_wetness = clamp(
            dot(weights, material.terrain_wetness),
            0.0,
            1.0,
        );
        if ((terrain_flags() & 2048u) != 0u) {
            authored_wetness = max(authored_wetness, terrain_material_b.r);
        }
        let porosity = clamp(dot(weights, material.terrain_porosity), 0.0, 1.0);
        var puddle = 0.0;
        if ((terrain_flags() & 512u) != 0u && material.terrain_height.y > 0.0) {
            let height = sample_rage(
                terrain_height_texture,
                material_sampler,
                terrain_lookup_uv(input),
            ).r * material.terrain_height.z + material.terrain_height.w;
            puddle = clamp(
                (height + material.terrain_height.x) * material.terrain_height.y,
                0.0,
                1.0,
            );
        }
        // Authored wetness and puddle masks describe how the surface responds to rain; the weather
        // decides how wet it actually is. Dry weather (the default) leaves terrain at its authored roughness.
        wetness_response = max(authored_wetness * (1.0 - 0.65 * porosity), puddle);
        terrain_wetness = wetness_response * 0.0;
        // Retail terrain wetting: luminance follows 0.305y^3 + 0.45y^2 + 0.135y + 0.01 and smoothness
        // follows 0.26s^3 - 0.915s^2 + 1.2s + 0.4, each blended in by the wet amount.
        let luminance = dot(albedo.rgb, vec3f(0.299, 0.587, 0.114)) + 0.000001;
        let wet_luminance = ((0.305 * luminance + 0.45) * luminance + 0.135) * luminance + 0.01;
        albedo = vec4f(albedo.rgb * mix(luminance, wet_luminance, terrain_wetness) / luminance, albedo.a);
        let smoothness = 1.0 - clamp(surface_roughness, 0.0, 1.0);
        let wet_smoothness = ((0.26 * smoothness - 0.915) * smoothness + 1.2) * smoothness + 0.4;
        surface_roughness = 1.0 - mix(smoothness, wet_smoothness, terrain_wetness);
    }
    surface_roughness = clamp(
        surface_roughness + material.pbr_tweaks.x,
        0.08,
        1.0,
    );
    surface_metallic = clamp(
        surface_metallic + material.pbr_tweaks.y,
        0.0,
        1.0,
    );
    surface_ao = clamp(surface_ao + material.pbr_tweaks.z, 0.0, 1.0);
    let debug_mode = 0u;
    if (debug_mode == 1u) {
        return vec4f(vec3f(final_alpha), 1.0);
    }
    if (debug_mode == 2u) {
        return vec4f(vec3f(sampled_colour.a), 1.0);
    }
    if (debug_mode == 3u) {
        return vec4f(vec3f(input.colour.a), 1.0);
    }
    if (debug_mode == 4u) {
        return vec4f(material.decal_opacity.rgb, 1.0);
    }
    if (debug_mode == 5u) {
        return vec4f(vec3f(dirt_mask), 1.0);
    }
    if (debug_mode == 6u) {
        return vec4f(palette_tint, 1.0);
    }
    if (debug_mode == 7u) {
        return vec4f(packed_material, 1.0);
    }
    if (debug_mode == 8u) {
        return vec4f(vec3f(specular_texture_intensity), 1.0);
    }
    if (debug_mode == 9u) {
        return vec4f(weights.rgb, 1.0);
    }
    if (debug_mode == 10u) {
        return vec4f(
            sample_rage(
                terrain_tint_texture,
                material_sampler,
                terrain_lookup_uv(input),
            ).rgb,
            1.0,
        );
    }
    if (debug_mode == 11u) {
        return vec4f(vec3f(wetness_response), 1.0);
    }
    if (debug_mode == 13u) {
        return vec4f(terrain_material_b.rgb, 1.0);
    }
    // Cut-outs in the main view with the game's sub-sampled alpha (Rdr2/cutout.wgsl), elsewhere by the alpha test.
    var sub_sample_mark = 1.0;
    if (alpha_mode == 1u) {
        if (sub_sampled_view && rdr2_sub_sampled(material.flags.w)) {
            if (rdr2_sub_sample_drops(input.position.xy, albedo.a, material.flags.w)) {
                discard;
            }
            sub_sample_mark = rdr2_sub_sample_mark(rdr2_cutout_coverage(albedo.a, material.flags.w));
        } else if (albedo.a < material.emission_alpha_layers.y) {
            discard;
        }
    }

    var n = sample_normal(input, weights);
    if (!front_facing) {
        n = -n;
    }
    if (debug_mode == 12u) {
        return vec4f(n * 0.5 + 0.5, 1.0);
    }
    let is_normal_only_decal = (material.flags.w & 4u) != 0u;
    if (is_normal_only_decal) {
        var receiver_normal = normalize(input.normal);
        if (!front_facing) {
            receiver_normal = -receiver_normal;
        }
        let receiver = max(decal_irradiance(receiver_normal), vec3f(0.05));
        let bumped = decal_irradiance(n);
        let coverage = clamp(
            albedo.a * material.decal_opacity.y,
            0.0,
            1.0,
        );
        let modulation = mix(
            vec3f(1.0),
            clamp(bumped / receiver, vec3f(0.0), vec3f(2.0)),
            coverage,
        );
        return vec4f(modulation, 1.0);
    }
    let v = normalize(rdr2_eye() - input.world_position);
    let l = light.sun_direction.xyz;
    let h = normalize(v + l);
    let ndotl = max(dot(n, l), 0.0);
    let ndotv = max(dot(n, v), 0.0001);
    let roughness = surface_roughness;
    let metallic = surface_metallic;
    let base_colour = albedo.rgb;
    var f0 = clamp(
        mix(vec3f(0.04), base_colour, metallic)
            + vec3f(material.pbr_specular.w),
        vec3f(0.0),
        vec3f(1.0),
    );
    if (native_terrain_blend()) {
        f0 = vec3f(clamp(1.0 - dot(weights, material.terrain_fresnel), 0.0, 0.08));
    } else if (lod_terrain_encoding() && material_map_mask != 0u) {
        // GBuffer2.x = saturate(SpecularTexture.R); the lighting pass reads f0 = 0.08 * GBuffer2.x.
        f0 = vec3f(0.08 * clamp(packed_material.r, 0.0, 1.0));
    }
    let fresnel_edge = fresnel_schlick(max(dot(h, v), 0.0), f0);
    let terrain_fresnel = select(
        material.layer_fresnel,
        material.terrain_fresnel,
        (terrain_flags() & 1u) != 0u,
    );
    let fresnel_strength = clamp(
        dot(weights, terrain_fresnel) + terrain_wetness * 0.2,
        0.0,
        1.0,
    );
    // Retail deferred terrain (HD and LOD) gets the full Schlick term from the tiled lighting pass.
    let retail_terrain = native_terrain_blend() || lod_terrain_encoding();
    let fresnel = select(mix(f0, fresnel_edge, fresnel_strength), fresnel_edge, retail_terrain);
    // The sun is a disk, not a point: widen the lobe by its angular radius and renormalize (Karis 2013),
    // so mirror-like wet surfaces show a sun-sized highlight instead of a sub-pixel spike.
    let alpha = roughness * roughness;
    let sun_alpha = clamp(alpha + 0.004756 * 0.5, 0.0, 1.0);
    let distribution = distribution_ggx(n, h, sqrt(sun_alpha)) * (alpha / sun_alpha) * (alpha / sun_alpha);
    let visibility = visibility_smith_schlick(ndotv, ndotl, roughness);
    let specular_scale = material.pbr_specular.x * specular_texture_intensity;
    let brdf_scale_bias = environment_brdf(ndotv, roughness);
    let specular = distribution * visibility * fresnel * specular_scale;
    let diffuse = (1.0 - metallic) * base_colour * diffuse_burley(ndotv, ndotl, max(dot(l, h), 0.0), roughness);

    var ao = surface_ao;
    if ((material.flags.y & 1u) != 0u) {
        ao *= sample_rage(occlusion_texture, material_sampler, input.uv).r;
    }
    // The viewer has no screen-space mask: every surface reads the cascades (light.wgsl's sun_shadow), on the side of
    // its geometric normal being drawn. A world's screen-space occlusion is taken out after (ambient_occlusion.wgsl).
    let screen_occlusion = 1.0;
    let sun_visibility = sun_shadow(input.world_position, select(-normalize(input.normal), normalize(input.normal), front_facing));
    // Baked and screen-space occlusion describe the same missing light; combine with min, not product.
    let ambient_visibility = min(ao, screen_occlusion);
    let diffuse_colour = base_colour * (1.0 - metallic);
    let irradiance = sky_irradiance(n);
    var lit = diffuse_colour * irradiance * multi_bounce_occlusion(ambient_visibility, diffuse_colour);
    occludable(diffuse_colour * irradiance, ao, n, diffuse_colour);
    // Specular ambient: analytic sky along the reflection, widened towards irradiance with roughness.
    let reflected = reflect(-v, n);
    // The sky-view LUT is unfiltered; rough lobes average most of the sky, so fade towards the
    // cosine-convolved harmonics along the reflection (Karis-style roughness blend) instead of
    // mirroring the bright Mie halo around the sun onto rough ground.
    let prefiltered = mix(rdr2_sky(reflected, roughness), sky_irradiance(reflected), smoothstep(0.05, 0.55, roughness));
    // Horizon occlusion: normal-mapped reflections cannot see sky below the geometric surface.
    let geometric = select(-normalize(input.normal), normalize(input.normal), front_facing);
    let horizon = clamp(1.0 + 1.3 * dot(reflected, geometric), 0.0, 1.0);
    let environment = prefiltered * horizon * horizon;
    // The split-sum bias is the Fresnel edge term; authored fresnel strength attenuates it as it does for the sun.
    let edge_strength = select(fresnel_strength, 1.0, retail_terrain);
    lit += environment * (f0 * brdf_scale_bias.x + brdf_scale_bias.y * edge_strength * fresnel_f90(f0))
        * specular_occlusion(ndotv, ambient_visibility, roughness) * clamp(specular_scale, 0.0, 1.0);
    lit += (diffuse + specular)
        * rdr2_sun()
        * ndotl
        * sun_visibility;
    lit += local_irradiance(input.position.xy, input.relative_position, n, v, base_colour,
        f0, roughness, metallic, specular_scale, edge_strength);
    if (material.flags.z != 0u) {
        lit += sample_rage(emissive_texture, material_sampler, input.uv).rgb
            * material.emission_alpha_layers.x;
    }
    var output_alpha = sub_sample_mark;
    if (alpha_mode == 2u) {
        output_alpha = albedo.a;
    }
    if (is_decal) {
        output_alpha = final_alpha;
        if (output_alpha <= 0.001) {
            discard;
        }
    }
    lit = apply_aerial_perspective(lit, input.world_position, input.position.xy);
    return vec4f(lit, output_alpha);
}
