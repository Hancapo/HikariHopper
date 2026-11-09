// GTA V's terrain, as its terrain_cb_* shaders draw it: PS_TexturedDeferred over four layers, each a diffuse map and
// a bump map, mixed by three weights. The terrain writes what the game writes to its G-buffer, for dry weather, and is
// lit with the game's lighting passes (light.wgsl's gta_lit), as surface.wgsl's families are.
// Scene comes from scene.wgsl, the light from light.wgsl.
//
// The weights (x, y, z) come from the vertex's second colour (the cb shaders), from a lookup map on the second UV set
// (_cm), or from the lookup map pulled towards the second colour by the first colour's alpha (_2tex_blend). They mix
// the layers as
//   layer 0 (1-x)(1-y)(1-z), layer 1 (1-x)(1-y)z, layer 2 (1-x)y(1-z), layer 3 (1-x)yz,
// the diffuse maps by the weights saturated, the bump maps by them as they are. The layers are on the first UV set,
// but for _2tex, which reads layers 1 to 3 on the second.
//
// The _pxm shaders add parallax occlusion mapping: each layer's height map (weighted as its bump map) is marched along
// the view in tangent space, near the camera only, and the layers are read where the view meets the heights. Their
// vertices carry the parallax controls in the set after their UVs. The _spm ones read a specular map from the height
// maps' green.
//
// What the game writes to the G-buffer's self-shadow (gBuffer2.w), which its directional pass multiplies the sun by,
// is a bump self-shadow from the vertex's angle to the sun and the albedo's brightness, and near the camera the
// parallax's own.

struct Terrain {
    // specularIntensityMult, specularFalloffMult, bumpiness, fresnel (the program's 0.98).
    specular: vec4f,
    // Where the weights come from (0 the vertex, 1 the lookup map blended with the vertex, 2 the lookup map), has bump
    // maps (the _lod shaders don't), has a tint palette, tintPaletteSelector.
    options: vec4f,
    // Layers 1 to 3 on the second UV set (_2tex), bumpSelfShadowAmount, has parallax (_pxm), parallaxSelfShadowAmount.
    layers: vec4f,
    // heightScale0 to 3, then heightBias0 to 3.
    height_scale: vec4f,
    height_bias: vec4f,
    // Has a specular map (_spm), specularIntensityMultSpecMap, specIntensityAdjust, specularFalloffMultSpecMap.
    spec_map: vec4f,
    // specFalloffAdjust, globalHeightScale.
    spec_map2: vec4f,
};

@group(1) @binding(0) var<uniform> terrain: Terrain;
@group(1) @binding(1) var terrain_sampler: sampler;
@group(1) @binding(2) var diffuse0: texture_2d<f32>;
@group(1) @binding(3) var diffuse1: texture_2d<f32>;
@group(1) @binding(4) var diffuse2: texture_2d<f32>;
@group(1) @binding(5) var diffuse3: texture_2d<f32>;
@group(1) @binding(6) var bump0: texture_2d<f32>;
@group(1) @binding(7) var bump1: texture_2d<f32>;
@group(1) @binding(8) var bump2: texture_2d<f32>;
@group(1) @binding(9) var bump3: texture_2d<f32>;
@group(1) @binding(10) var lookup_map: texture_2d<f32>;
@group(1) @binding(11) var tint_palette: texture_2d<f32>;
@group(1) @binding(12) var palette_sampler: sampler;
@group(1) @binding(13) var height0: texture_2d<f32>;
@group(1) @binding(14) var height1: texture_2d<f32>;
@group(1) @binding(15) var height2: texture_2d<f32>;
@group(1) @binding(16) var height3: texture_2d<f32>;

struct TerrainInput {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec4f,
    @location(3) uv0: vec2f,
    @location(4) uv1: vec2f,
    @location(5) colour0: vec4f,
    @location(6) colour1: vec4f,
    @location(7) uv2: vec2f,
};

struct TerrainOutput {
    @builtin(position) position: vec4f,
    // Relative to the scene's origin.
    @location(0) place: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec3f,
    @location(3) binormal: vec3f,
    // The first UV set, then the second.
    @location(4) uv: vec4f,
    // Natural and artificial ambient (red and green), and the _2tex_blend shaders' blend in alpha.
    @location(5) colour0: vec4f,
    // The layer weights.
    @location(6) colour1: vec4f,
    // The tint palette's colour, white for untinted shaders.
    @location(7) tint: vec3f,
    // Drawn into the reflection map: how far into the hemisphere being drawn the point is; behind it below zero.
    @location(8) hemisphere: f32,
    // The _pxm shaders' parallax controls: how much of the height to keep (one less x), and how flat a view may go
    // before the parallax stops growing (one less y).
    @location(9) controls: vec2f,
    // How far the point is from 8 m ahead of the camera, which sets how much parallax there is.
    @location(10) distance: f32,
    // The vertex's angle to the sun less bumpSelfShadowAmount, times nine: the bump self-shadow's start.
    @location(11) bump_shadow: f32,
};

// The camera's reflection map being drawn, as surface.wgsl reads it.
struct ReflectionView {
    eye: vec4f,
    side: vec4f,
};

@group(3) @binding(0) var<uniform> reflection_view: ReflectionView;

@vertex
fn vs_terrain(input: TerrainInput) -> TerrainOutput {
    var output = terrain_vertex(input);
    output.position = scene.view_projection * vec4f(input.position, 1.0);
    return output;
}

// Into the reflection map, as surface.wgsl's vs_surface_paraboloid.
@vertex
fn vs_terrain_paraboloid(input: TerrainInput) -> TerrainOutput {
    var output = terrain_vertex(input);
    let offset = input.position - reflection_view.eye.xyz;
    let distance = max(length(offset), 1e-5);
    let d = offset / distance;
    let side = reflection_view.side.x;
    let z = d.z * side;
    let p = d.xy / max(1.0 + z, 1e-3);
    output.position = vec4f(p.x * side, p.y, saturate(distance / reflection_view.eye.w), 1.0);
    output.hemisphere = z;
    return output;
}

fn terrain_vertex(input: TerrainInput) -> TerrainOutput {
    var output: TerrainOutput;
    output.hemisphere = 1.0;
    output.place = input.position;
    output.normal = normalize(input.normal);
    output.tangent = input.tangent.xyz;
    output.binormal = cross(input.tangent.xyz, output.normal) * input.tangent.w;
    output.uv = vec4f(input.uv0, input.uv1);
    output.colour0 = input.colour0;
    output.colour1 = input.colour1;
    output.controls = input.uv2;
    // As VS_TransformDeferred: the distance to a point 8 m ahead of the camera, and the bump self-shadow's start.
    output.distance = length(scene.eye.xyz + 8.0 * scene.forward.xyz - (input.position + scene.origin.xyz));
    output.bump_shadow = (saturate(dot(output.normal, light.sun_direction.xyz)) - terrain.layers.y) * 9.0;
    // The _tnt shaders: the palette read at the vertex blue's column, the selector's row.
    output.tint = vec3f(1.0);
    if (terrain.options.z > 0.5) {
        output.tint = textureSampleLevel(tint_palette, palette_sampler, vec2f(input.colour0.b, terrain.options.w), 0.0).rgb;
    }
    return output;
}

@fragment
fn fs_terrain(input: TerrainOutput) -> @location(0) vec4f {
    return shade_terrain(input);
}

// Into the reflection map: what is behind the hemisphere is left out, after shading.
@fragment
fn fs_terrain_reflected(input: TerrainOutput) -> @location(0) vec4f {
    let colour = shade_terrain(input);
    if (input.hemisphere < 0.0) {
        discard;
    }
    return colour;
}

// The four layers' shares for weights (x, y, z).
fn shares(w: vec3f) -> vec4f {
    let a = (1.0 - w.x) * (1.0 - w.y);
    let b = (1.0 - w.x) * w.y;
    return vec4f(a * (1.0 - w.z), a * w.z, b * (1.0 - w.z), b * w.z);
}

// The layers' heights, weighted, with the parallax offset added to each layer's UV set.
fn height_at(uv: vec4f, offset: vec2f, s: vec4f) -> f32 {
    let first = uv.xy + offset;
    let rest = select(uv.xy, uv.zw, terrain.layers.x > 0.5) + offset;
    return textureSampleLevel(height0, terrain_sampler, first, 0.0).x * s.x
        + textureSampleLevel(height1, terrain_sampler, rest, 0.0).x * s.y
        + textureSampleLevel(height2, terrain_sampler, rest, 0.0).x * s.z
        + textureSampleLevel(height3, terrain_sampler, rest, 0.0).x * s.w;
}

struct Parallax {
    offset: vec2f,
    shadow: f32,
};

// PS_TexturedDeferred's parallax occlusion mapping, as the _pxm programs compile it: none past 20 m from the point
// ahead of the camera, fewer steps further off and the more the view faces the surface (from pomWeights), and the
// height fading out from 13 m to 20 m. The march stops at the first step under the heights and meets them between
// that step and the last; a self-shadow is then marched towards the light, when its tangent-space direction leans
// along the normal, as the program tests it.
fn parallax(input: TerrainOutput, weights: vec3f) -> Parallax {
    var result: Parallax;
    result.offset = vec2f(0.0);
    result.shadow = 1.0;
    if (terrain.layers.z < 0.5) {
        return result;
    }
    let n = input.normal;
    let v = scene.eye.xyz - (input.place + scene.origin.xyz);
    let n_dot_v = saturate(dot(normalize(v), normalize(n)));
    let view = vec3f(dot(input.tangent, v), dot(input.binormal, v), dot(n, v));
    let keep = 1.0 - saturate(input.controls.x);
    let flattest = 1.0 - clamp(input.controls.y, 0.1, 1.0);
    let near = saturate(input.distance * 0.05);
    let fade = saturate((input.distance - 13.0) / 7.0);

    var pom_weights = array<f32, 5>(1.0, 0.8, 0.5, 0.1, 0.0);
    var weight = 0.0;
    if (near != 1.0) {
        let f = near * 5.0;
        let i = clamp(i32(f), 0, 4);
        let j = clamp(i + 1, 0, 4);
        weight = mix(pom_weights[i], pom_weights[j], f - f32(i32(f)));
    }
    let steps_f = weight * (27.0 - 24.0 * n_dot_v);
    let steps = i32(steps_f);
    if (steps < 1) {
        return result;
    }
    let amount = (1.0 - fade) * keep * saturate(steps_f - 1.0) * saturate((n_dot_v - 0.1) * 4.0) * terrain.spec_map2.y;
    if (amount <= 0.0) {
        return result;
    }
    let s = shares(weights);
    let scale = dot(terrain.height_scale, s) * amount;
    if (scale == 0.0) {
        return result;
    }
    let bias = dot(terrain.height_bias, s);
    let step = 1.0 / f32(steps);
    let tangent_view = normalize(view);
    let lean = max(select(flattest, 0.1, flattest == 0.0), tangent_view.z);
    let reach = -tangent_view.xy / lean * scale;
    let start = bias * amount * (tangent_view.xy / lean);
    let advance = reach * step;

    let h0 = height_at(input.uv, vec2f(0.0), s) + 1e-6;
    var layer = 1.0;
    var previous_layer = 1.0;
    var offset = start;
    var h = h0;
    var previous_h = h0;
    for (var i = 0; i < steps; i++) {
        if (h >= layer) {
            break;
        }
        previous_layer = layer;
        previous_h = h;
        layer -= step;
        offset += advance;
        h = height_at(input.uv, offset, s);
    }
    let after = layer - h;
    let before = previous_layer - previous_h;
    var t = 1.0;
    if (before - after != 0.0) {
        t = (before * layer - after * previous_layer) / (before - after);
    }
    result.offset = (1.0 - t) * reach + start;

    // The self-shadow, towards the way the light travels (gDirectionalLight) in tangent space.
    let travel = -light.sun_direction.xyz;
    let towards = normalize(vec3f(dot(input.tangent, travel), dot(input.binormal, travel), dot(n, travel)));
    if (dot(n, towards) > 0.0 && terrain.layers.w > 0.0) {
        let along = towards.xy / towards.z * scale;
        var deepest = 0.0;
        for (var k = 0; k < 7; k++) {
            let f = 1.0 - f32(k) / 7.0;
            deepest = max(deepest, (height_at(input.uv, along * f, s) - h0 - f) * f32(k + 1));
        }
        result.shadow = 1.0 - terrain.layers.w * saturate(deepest);
    }
    return result;
}

fn shade_terrain(input: TerrainOutput) -> vec4f {
    // The weights, from where the shader takes them.
    let lookup = textureSample(lookup_map, terrain_sampler, input.uv.zw).rgb;
    let source = u32(terrain.options.x + 0.5);
    var weights = input.colour1.rgb;
    if (source == 1u) {
        weights = mix(lookup, input.colour1.rgb, input.colour0.a);
    } else if (source == 2u) {
        weights = lookup;
    }
    let diffuse_shares = shares(saturate(weights));
    let bump_shares = shares(weights);

    // Where the layers are read: their UV sets, moved by the parallax.
    let par = parallax(input, weights);
    let first = input.uv.xy + par.offset;
    let rest = select(input.uv.xy, input.uv.zw, terrain.layers.x > 0.5) + par.offset;
    let d0 = textureSample(diffuse0, terrain_sampler, first);
    let d1 = textureSample(diffuse1, terrain_sampler, rest);
    let d2 = textureSample(diffuse2, terrain_sampler, rest);
    let d3 = textureSample(diffuse3, terrain_sampler, rest);
    let b0 = textureSample(bump0, terrain_sampler, first);
    let b1 = textureSample(bump1, terrain_sampler, rest);
    let b2 = textureSample(bump2, terrain_sampler, rest);
    let b3 = textureSample(bump3, terrain_sampler, rest);
    let h0 = textureSample(height0, terrain_sampler, first);
    let h1 = textureSample(height1, terrain_sampler, rest);
    let h2 = textureSample(height2, terrain_sampler, rest);
    let h3 = textureSample(height3, terrain_sampler, rest);

    // Albedo as stored: the layers mixed, by the tint palette's colour for the _tnt shaders.
    let stored = (d0.rgb * diffuse_shares.x + d1.rgb * diffuse_shares.y + d2.rgb * diffuse_shares.z + d3.rgb * diffuse_shares.w) * input.tint;

    // The normal: the layers' bump XY mixed, Z rebuilt from it, XY scaled by bumpiness (at least 0.001) in the
    // tangent frame.
    var normal = normalize(input.normal);
    if (terrain.options.y > 0.5) {
        let mixed = b0.xy * bump_shares.x + b1.xy * bump_shares.y + b2.xy * bump_shares.z + b3.xy * bump_shares.w;
        let xy = mixed * 2.0 - 1.0;
        let z = sqrt(abs(1.0 - dot(xy, xy)));
        let scale = max(0.001, terrain.specular.z);
        normal = normalize(scale * xy.x * input.tangent + scale * xy.y * input.binormal + z * input.normal);
    }

    // Specular: specularIntensityMult squared, specularFalloffMult as the power, and the fixed fresnel (dry; the
    // game's wetness raises both where it rains). The _spm shaders take both from the height maps' green instead,
    // squared less one, by their adjustments.
    var intensity = terrain.specular.x * terrain.specular.x;
    var power = terrain.specular.y;
    if (terrain.spec_map.x > 0.5) {
        let green = dot(vec4f(h0.g, h1.g, h2.g, h3.g), bump_shares);
        let a = green * green - 1.0;
        intensity = (a * terrain.spec_map.z + 1.0) * terrain.spec_map.y;
        power = terrain.spec_map.w * (a * terrain.spec_map2.x + 1.0);
    }

    // The self-shadow the directional pass multiplies the sun by: the bump self-shadow, kept off bright albedo; near
    // the camera, the _pxm shaders' parallax self-shadow in its place.
    let bump_shadow = saturate(input.bump_shadow - 0.5 + dot(stored, vec3f(1.2750001, 4.2923002, 0.43260002)));
    var self_shadow = bump_shadow;
    if (terrain.layers.z > 0.5) {
        let near = saturate(input.distance * 0.05);
        if (near != 1.0) {
            self_shadow = select(par.shadow, mix(par.shadow, bump_shadow, (near - 0.8) * 5.0), near > 0.8);
        }
    }

    // The natural ambient the program stores is the draw's ambient level plus the vertex red, unscaled (where the
    // other families scale the red by the occlusion multiplier): taken here as one less the multiplier, plus the red.
    let ambient = saturate(1.0 - light.natural_up.w + input.colour0.r);

    let view = normalize(scene.eye.xyz - (input.place + scene.origin.xyz));
    let shadow = sun_shadow(input.place, normalize(input.normal)) * self_shadow;
    return vec4f(gta_lit(stored, normal, view, intensity, power, terrain.specular.w, ambient * ambient, shadow), 1.0);
}
