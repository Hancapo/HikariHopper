// GTA V's terrain, as its terrain_cb_* shaders draw it: PS_TexturedDeferred over four layers, each a diffuse map and
// a bump map on the first UV set, mixed by three weights. The terrain writes what the game writes to its G-buffer,
// for dry weather, and is lit with the game's lighting passes (light.wgsl's gta_lit), as surface.wgsl's families are.
// Scene comes from scene.wgsl, the light from light.wgsl.
//
// The weights (x, y, z) come from the vertex's second colour (the cb shaders), from a lookup map on the second UV set
// (_cm), or from the lookup map pulled towards the vertex colour by its alpha (_2tex_blend). They mix the layers as
//   layer 0 (1-x)(1-y)(1-z), layer 1 (1-x)(1-y)z, layer 2 (1-x)y(1-z), layer 3 (1-x)yz,
// the diffuse maps by the weights saturated, the bump maps by them as they are.

struct Terrain {
    // specularIntensityMult, specularFalloffMult, bumpiness, fresnel (the program's 0.98).
    specular: vec4f,
    // Where the weights come from (0 the vertex, 1 the lookup map blended with the vertex, 2 the lookup map), has bump
    // maps (the _lod shaders don't), has a tint palette, tintPaletteSelector.
    options: vec4f,
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

struct TerrainInput {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec4f,
    @location(3) uv0: vec2f,
    @location(4) uv1: vec2f,
    @location(5) colour0: vec4f,
    @location(6) colour1: vec4f,
};

struct TerrainOutput {
    @builtin(position) position: vec4f,
    // Relative to the scene's origin.
    @location(0) place: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec3f,
    @location(3) binormal: vec3f,
    // The layers' UV set, then the lookup map's.
    @location(4) uv: vec4f,
    // Natural and artificial ambient (red and green).
    @location(5) colour0: vec4f,
    // The layer weights, and in alpha how far the vertex's override the lookup map's (_2tex_blend).
    @location(6) colour1: vec4f,
    // The tint palette's colour, white for untinted shaders.
    @location(7) tint: vec3f,
    // Drawn into the reflection map: how far into the hemisphere being drawn the point is; behind it below zero.
    @location(8) hemisphere: f32,
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

fn shade_terrain(input: TerrainOutput) -> vec4f {
    // Every map is sampled before anything is decided, as derivatives need uniform control flow.
    let uv = input.uv.xy;
    let d0 = textureSample(diffuse0, terrain_sampler, uv);
    let d1 = textureSample(diffuse1, terrain_sampler, uv);
    let d2 = textureSample(diffuse2, terrain_sampler, uv);
    let d3 = textureSample(diffuse3, terrain_sampler, uv);
    let b0 = textureSample(bump0, terrain_sampler, uv);
    let b1 = textureSample(bump1, terrain_sampler, uv);
    let b2 = textureSample(bump2, terrain_sampler, uv);
    let b3 = textureSample(bump3, terrain_sampler, uv);
    let lookup = textureSample(lookup_map, terrain_sampler, input.uv.zw).rgb;

    // The weights, from where the shader takes them.
    let source = u32(terrain.options.x + 0.5);
    var weights = input.colour1.rgb;
    if (source == 1u) {
        weights = mix(lookup, input.colour1.rgb, input.colour1.a);
    } else if (source == 2u) {
        weights = lookup;
    }
    let diffuse_shares = shares(saturate(weights));
    let bump_shares = shares(weights);

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
    // game's wetness raises both where it rains).
    let intensity = terrain.specular.x * terrain.specular.x;

    // The natural ambient the program stores is the draw's ambient level plus the vertex red, unscaled (where the
    // other families scale the red by the occlusion multiplier): taken here as one less the multiplier, plus the red.
    let scale = saturate(1.0 - light.natural_up.w + input.colour0.r);

    let view = normalize(scene.eye.xyz - (input.place + scene.origin.xyz));
    let shadow = sun_shadow(input.place, normalize(input.normal));
    return vec4f(gta_lit(stored, normal, view, intensity, terrain.specular.y, terrain.specular.w, scale * scale, shadow), 1.0);
}
