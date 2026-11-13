// GTA V's fur grass (grass_fur, grass_fur_mask, grass_fur_tnt), as its programs draw it: eight shells, each the whole
// mesh again, pushed out along the vertex normal by a step per shell, and cut out where that shell's channel of the
// combo height maps is too low for the fur there. The game draws them as eight passes of the cut-out technique, each
// with its own vertex and pixel program (VS_FurTransformD_Zero..Seven, PS_FurDeferredTextured_Zero..Seven); here
// they are eight instances of one draw, the instance being the shell. Each writes what the game writes to its
// G-buffer, for dry weather, and is lit with the game's lighting passes (light.wgsl's gta_lit), as surface.wgsl's
// families are.
// Scene comes from scene.wgsl, the light from light.wgsl.
//
// Shell i reads the green (even i) or the alpha (odd i) of combo height map i / 2, at mip bias 1.15 as the game's
// sampler does, times the fur amount (the vertex alpha; for _mask, the mask map's red too), and is cut out where that
// is under its clip (furAlphaClip03/47, by the amount). It is shaded from furLayerParams.w at no fur to its own shade
// (furShadow03/47) at full fur, darker the deeper it is.
//
// The game fades the whole prop out between furAlphaDistance's near and far (15 to 30 m in its props), by a noise,
// as a LOD takes over; the viewer shows a model on its own, from any distance, so it is drawn as if near. Nothing
// tramples it, and the game's wetness is off.

struct Fur {
    // specularFresnel, specularFalloffMult, specularIntensityMult, bumpiness.
    specular: vec4f,
    // specMapIntMask.
    mask: vec4f,
    // furLayerParams: the step between shells in metres, two values no program reads, and the shade with no fur.
    layer: vec4f,
    // furUvScales: the tiling of the diffuse and specular maps, of the combo height maps, of the bump map, and of the
    // _mask shader's DiffuseHf map.
    uv_scales: vec4f,
    // Which shader (0 grass_fur, 1 grass_fur_mask, 2 grass_fur_tnt), tintPaletteSelector.
    options: vec4f,
    // furAlphaClip03 and furAlphaClip47: each shell's clip.
    clip0: vec4f,
    clip1: vec4f,
    // furShadow03 and furShadow47: each shell's shade.
    shade0: vec4f,
    shade1: vec4f,
};

@group(1) @binding(0) var<uniform> fur: Fur;
@group(1) @binding(1) var fur_sampler: sampler;
@group(1) @binding(2) var diffuse_map: texture_2d<f32>;
@group(1) @binding(3) var bump_map: texture_2d<f32>;
@group(1) @binding(4) var specular_map: texture_2d<f32>;
// ComboHeightTexFur01, 23, 45 and 67: two shells each, in green and alpha.
@group(1) @binding(5) var height01: texture_2d<f32>;
@group(1) @binding(6) var height23: texture_2d<f32>;
@group(1) @binding(7) var height45: texture_2d<f32>;
@group(1) @binding(8) var height67: texture_2d<f32>;
// grass_fur_mask: where there is fur (FurMaskTex, on the second UV set) and a colour over the diffuse map (DiffuseHfTex,
// on the third).
@group(1) @binding(9) var fur_mask: texture_2d<f32>;
@group(1) @binding(10) var diffuse_hf: texture_2d<f32>;
// grass_fur_tnt: the tint palette, read per vertex.
@group(1) @binding(11) var tint_palette: texture_2d<f32>;
@group(1) @binding(12) var palette_sampler: sampler;

// The shells the game draws: eight at its shader quality setting (two), the top one.
const SHELLS: u32 = 8u;

struct FurInput {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec4f,
    @location(3) uv0: vec2f,
    @location(4) uv1: vec2f,
    @location(5) colour0: vec4f,
    @location(7) uv2: vec2f,
};

struct FurOutput {
    @builtin(position) position: vec4f,
    // Relative to the scene's origin, pushed out to the shell.
    @location(0) place: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec3f,
    @location(3) binormal: vec3f,
    // The diffuse and specular maps' UVs, then the combo height maps'.
    @location(4) uv: vec4f,
    // The bump map's UVs, then the _mask shader's mask map's (the second UV set).
    @location(5) uv_bump: vec4f,
    // The _mask shader's DiffuseHf map's UVs, and the shell.
    @location(6) uv_hf: vec2f,
    @location(7) @interpolate(flat) shell: u32,
    @location(8) colour0: vec4f,
    // The tint palette's colour, white for the shaders that aren't tinted.
    @location(9) tint: vec3f,
    // Drawn into the reflection map: how far into the hemisphere being drawn the point is; behind it below zero.
    @location(10) hemisphere: f32,
};

struct ReflectionView {
    eye: vec4f,
    side: vec4f,
};

@group(3) @binding(0) var<uniform> reflection_view: ReflectionView;

@vertex
fn vs_fur(input: FurInput, @builtin(instance_index) shell: u32) -> FurOutput {
    var output = fur_vertex(input, shell);
    output.position = scene.view_projection * vec4f(output.place, 1.0);
    return output;
}

// Into the reflection map, as surface.wgsl's vs_surface_paraboloid.
@vertex
fn vs_fur_paraboloid(input: FurInput, @builtin(instance_index) shell: u32) -> FurOutput {
    var output = fur_vertex(input, shell);
    let offset = output.place - reflection_view.eye.xyz;
    let distance = max(length(offset), 1e-5);
    let d = offset / distance;
    let side = reflection_view.side.x;
    let z = d.z * side;
    let p = d.xy / max(1.0 + z, 1e-3);
    output.position = vec4f(p.x * side, p.y, saturate(distance / reflection_view.eye.w), 1.0);
    output.hemisphere = z;
    return output;
}

// VS_FurTransformD: the shell pushed out along the normal as stored (not normalised), the first not at all; the
// tangent frame as VS_Transform's; each map's UVs at its tiling.
fn fur_vertex(input: FurInput, shell: u32) -> FurOutput {
    var output: FurOutput;
    output.hemisphere = 1.0;
    output.shell = min(shell, SHELLS - 1u);
    output.place = input.position + f32(output.shell) * fur.layer.x * input.normal;
    output.normal = normalize(input.normal);
    output.tangent = input.tangent.xyz;
    output.binormal = cross(input.tangent.xyz, output.normal) * input.tangent.w;
    output.uv = vec4f(input.uv0 * fur.uv_scales.x, input.uv0 * fur.uv_scales.y);
    output.uv_bump = vec4f(input.uv0 * fur.uv_scales.z, input.uv1);
    output.uv_hf = input.uv2 * fur.uv_scales.w;
    output.colour0 = input.colour0;
    // grass_fur_tnt: the palette read at the vertex blue's column, the selector's row.
    output.tint = vec3f(1.0);
    if (u32(fur.options.x + 0.5) == 2u) {
        output.tint = textureSampleLevel(tint_palette, palette_sampler, vec2f(input.colour0.b, fur.options.y), 0.0).rgb;
    }
    return output;
}

@fragment
fn fs_fur(input: FurOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    return shade_fur(input, front);
}

// Into the reflection map: the paraboloid mirrors the winding, and what is behind the hemisphere is left out.
@fragment
fn fs_fur_reflected(input: FurOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    let colour = shade_fur(input, !front);
    if (input.hemisphere < 0.0) {
        discard;
    }
    return colour;
}

// PS_FurDeferredTextured, shell by shell.
fn shade_fur(input: FurOutput, front: bool) -> vec4f {
    // Every map is sampled before anything is decided, as derivatives need uniform control flow. The combo height
    // maps' sampler has a mip bias of 1.15, the mask's and DiffuseHf's of -1.
    let diffuse = textureSample(diffuse_map, fur_sampler, input.uv.xy);
    let bump = textureSample(bump_map, fur_sampler, input.uv_bump.xy);
    let specular_sample = textureSample(specular_map, fur_sampler, input.uv.xy);
    let h01 = textureSampleBias(height01, fur_sampler, input.uv.zw, 1.15);
    let h23 = textureSampleBias(height23, fur_sampler, input.uv.zw, 1.15);
    let h45 = textureSampleBias(height45, fur_sampler, input.uv.zw, 1.15);
    let h67 = textureSampleBias(height67, fur_sampler, input.uv.zw, 1.15);
    let mask = textureSampleBias(fur_mask, fur_sampler, input.uv_bump.zw, -1.0);
    let hf = textureSampleBias(diffuse_hf, fur_sampler, input.uv_hf, -1.0);

    let shader = u32(fur.options.x + 0.5);
    let shell = input.shell;
    // This shell's channel: green for the first of a map's two, alpha for the second.
    var heights = array<vec4f, 4>(h01, h23, h45, h67);
    let height = heights[shell / 2u];
    let channel = select(height.g, height.a, (shell & 1u) == 1u);
    var clips = array<f32, 8>(fur.clip0.x, fur.clip0.y, fur.clip0.z, fur.clip0.w, fur.clip1.x, fur.clip1.y, fur.clip1.z, fur.clip1.w);
    var shades = array<f32, 8>(fur.shade0.x, fur.shade0.y, fur.shade0.z, fur.shade0.w, fur.shade1.x, fur.shade1.y, fur.shade1.z,
        fur.shade1.w);

    // How much fur there is: the vertex alpha, for _mask by the mask's red. Its height here is cut out under the
    // shell's clip, which is drawn towards a hundredth where there is little fur.
    let amount = input.colour0.a * select(1.0, mask.x, shader == 1u);
    let threshold = 0.01 + saturate(2.0 * amount) * (clips[shell] - 0.01);
    if (channel * amount < threshold) {
        discard;
    }

    // The albedo: the diffuse map by the shell's shade, from furLayerParams.w where there is no fur; for _mask by
    // DiffuseHf's colour, for _tnt by the palette's.
    var base = mix(fur.layer.w, shades[shell], amount) * diffuse.rgb * input.tint;
    if (shader == 1u) {
        base *= hf.rgb;
    }

    // The normal: the bump map's XY by bumpiness (at least 0.001) in the tangent frame, Z rebuilt; the back of a shell
    // seen from below faces the other way.
    let xy = bump.xy * 2.0 - 1.0;
    let scale = max(0.001, fur.specular.w);
    var normal = normalize(scale * xy.x * input.tangent + scale * xy.y * input.binormal + sqrt(abs(1.0 - dot(xy, xy))) * input.normal);
    normal = select(-normal, normal, front);

    // Specular: the map's red squared (by specMapIntMask) times specularIntensityMult; the power is its alpha times
    // specularFalloffMult; specularFresnel.
    let weighed = vec3f(specular_sample.r * specular_sample.r, specular_sample.g * specular_sample.g, specular_sample.b);
    let intensity = fur.specular.z * dot(weighed, fur.mask.xyz);
    let power = specular_sample.a * fur.specular.y;

    // The natural ambient: the vertex red is its occlusion, with no dynamic bake (the fur programs write none).
    let ambient = clamp(1.0 - light.natural_up.w * (1.0 - saturate(input.colour0.r)), 0.0, 2.0);
    let view = normalize(scene.eye.xyz - (input.place + scene.origin.xyz));
    let shadow = sun_shadow(input.place, select(-1.0, 1.0, front) * normalize(input.normal));
    return vec4f(gta_lit(base, normal, view, intensity, power, fur.specular.x, ambient * ambient, shadow, 1.0), 1.0);
}
