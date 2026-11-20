// Drawables, shaded as GTA V shades them. Three families of the game's deferred pixel shaders are rebuilt here, each
// writing what the game writes to its G-buffer (albedo, normal, specular intensity, power and fresnel, ambient scales):
//   - PS_DeferredTextured: default, normal, spec, normal_spec, emissive, decal, _tnt, _detail and the rest;
//   - PS_DeferredVehicleTextured: vehicle paint, mesh, shuts, tyres, interiors and the rest;
//   - PS_DeferredPedTextured: the peds' skin and clothes (ped, _wrinkle, _emissive…), their diffuse-only parts
//     (ped_default…) and their hair (ped_hair_spiked, ped_hair_cutout_alpha…), at rest: dry, undamaged, unwrinkled.
// That is then lit with the game's directional pass (PS_directional_just_dir) and the core of its ambient one
// (PS_directional_ambient). The game lights the G-buffer later; here both happen in one pass, with the same
// arithmetic. The light is the game's own, from its timecycle (light.wgsl): the directional light with its shadow, the
// natural ambient hemisphere and the directional ambient. The result is linear HDR with coverage, for tonemap.wgsl.
// A vehicle's glass is no deferred shader: the game draws it forward, blended, lit as it is drawn (PS_VehicleTextured,
// in vehicle_vehglass and its _crack and _inner), and so does glass() here.
// The trees' (the game's tree shaders, checked against Enhanced's) are their own: shade_tree, with the game's foliage
// light pass (PS_directional_scatter) for all but trees_lod2's billboards, which the game lights as any other surface.
// Scene comes from scene.wgsl, the light from light.wgsl, the trees' vertex work from trees.wgsl.

struct Material {
    // specularFresnel, specularFalloffMult, specularIntensityMult, bumpiness.
    specular: vec4f,
    // specMapIntMask, emissiveMultiplier.
    mask: vec4f,
    // Has a normal map, reads a specular map, alpha mode (0 opaque, 1 mask, 2 blend), is emissive.
    options: vec4f,
    // The cut-outs' alpha reference, has a tint palette, tintPaletteSelector, family (0 textured, 1 vehicle).
    extra: vec4f,
    // Vehicles: matDiffuseColor, matDiffuseSpecularRampEnabled.
    paint: vec4f,
    // Vehicles: dirtLevelMod (how dirty, how much of the dirt's colour, wet or ramp blend, second lobe strength).
    dirt_level: vec4f,
    // Vehicles: dirtColor, has a dirt map.
    dirt_colour: vec4f,
    // Vehicles: specular2Color_DirLerp, the second lobe's colour and how far its light swings overhead.
    specular2: vec4f,
    // Vehicles: is a paint, has the second lobe, has a diffuse ramp.
    vehicle: vec4f,
    // Vehicles: how the shader's program writes its specular (materials.json's vehicle_specular): the specular map's
    // part in the intensity (0 none, 1 masked, 2 red squared), the roughness (0 none, 1 the dirt map's, 2
    // dirtLevelMod.z), the map's part in the power (0 none, 1 alpha, 2 green squared), and whether fresnel is one
    // less the map's blue.
    spec_model: vec4f,
    // The UV set the specular map is read on and its tiling; has a second diffuse layer (a vehicle's), or the decal a
    // textured shader lays over itself (1 by its alpha, _texdecal; 2 by one less the vertex alpha, _vertdecal); the UV
    // set it is read on.
    spec_place: vec4f,
    // The second diffuse layer's colour and strength (matDiffuseColor2).
    layer: vec4f,
    // The _detail shaders: detailSettings (how much the detail darkens, how much it bumps, its tiling in U and V).
    detail: vec4f,
    // Has a detail map, what masks it (0 nothing, 1 the specular map's alpha, 2 the diffuse map's).
    detail_options: vec4f,
    // Where the specular comes from besides the constants and a specular map: 0 nowhere else, 1 the diffuse map's
    // luminance (emissive_speclum), 2 an overlay of the diffuse map's colour (diffspec).
    spec_source: vec4f,
    // The diffspec shaders' SpecDesaturateIntensity and SpecDesaturateExponent: weights for the diffuse colour, then
    // the overlay's strength.
    desaturate_intensity: vec4f,
    desaturate_exponent: vec4f,
    // The _pxm shaders: has parallax, heightScale, heightBias, parallaxSelfShadowAmount.
    parallax: vec4f,
    // globalHeightScale; whether it is a decal, whose alpha is the diffuse map's times the vertex colour's; a cut-out's
    // HardAlphaBlend, how hard its sub-sampled alpha is.
    parallax_global: vec4f,
    // Peds: which of PS_DeferredPedTextured's programs (0 not a ped, 1 skin and clothes, 2 diffuse only, 3 hair),
    // whether it has the rim light and the bend towards the view (all but the forward ped_alpha), whether the diffuse
    // alpha is a palette index (_palette), whether hair has a diffuse mask (_mask).
    ped: vec4f,
    // Hair: anisotropicSpecularIntensity and anisotropicSpecularExponent, the two highlights' strengths and exponents.
    hair: vec4f,
    // Hair: anisotropicSpecularColour, and AnisotropicAlphaBias.
    hair_colour: vec4f,
    // Hair: specularNoiseMapUVScaleFactor, the noise map's tiling for each highlight.
    hair_noise: vec4f,
    // Glass: which of PS_VehicleTextured's programs (0 not glass, 1 vehicle_vehglass, 2 _crack, 3 _inner).
    glass: vec4f,
    // Glass: matDiffuseColorTint, which the vertex alpha blends the diffuse towards, and whose alpha it adds.
    glass_tint: vec4f,
    // The _reflect shaders' environment (0 none, 1 a sphere map, 2 a cube) and reflectivePower; whether the emissive
    // (and a decal's alpha) is night-only (the emissivenight shaders), whether the geometry is (_geomnightonly).
    environment: vec4f,
    // Trees (trees.wgsl's TreeSettings): program (0 not a tree, 1 foliage, 2 lod, 3 billboard), alignment, AlphaScale,
    // AlphaTest; UseTreeNormals, SelfShadowing, ShadowFalloff; treeLod2Params.xy, ShadowFacingOffset, whether the
    // billboards are trees_lod2d's; treeLod2Normal.
    tree: vec4f,
    tree_shade: vec4f,
    tree_size: vec4f,
    tree_normal: vec4f,
};

fn tree_settings() -> TreeSettings {
    return TreeSettings(material.tree, material.tree_shade, material.tree_size, material.tree_normal);
}

// The camera's axes, as the trees turn to them.
fn tree_view() -> TreeView {
    return TreeView(scene.right.xyz, scene.up.xyz, -scene.forward.xyz);
}

// A tree's vertex as its vertex shader moves it, in its model's space; axes are its placement's rotation.
fn tree_input(input: SurfaceInput, axes: mat3x3f) -> SurfaceInput {
    if (material.tree.x < 0.5) {
        return input;
    }
    let moved = tree_vertex(tree_settings(), input.position, input.normal, input.tangent, input.uv0, input.uv1, input.uv2, axes, tree_view());
    var output = input;
    output.position = moved.position;
    output.normal = moved.normal;
    output.uv0 = moved.uv;
    return output;
}

// The tint palette's column: the vertex colour's blue, the second's for the trees.
fn tint_column(input: SurfaceInput) -> f32 {
    return select(input.colour0.b, input.colour1.b, material.tree.x > 0.5);
}

@group(1) @binding(0) var<uniform> material: Material;
@group(1) @binding(1) var surface_sampler: sampler;
@group(1) @binding(2) var diffuse_map: texture_2d<f32>;
@group(1) @binding(3) var normal_map: texture_2d<f32>;
@group(1) @binding(4) var specular_map: texture_2d<f32>;
@group(1) @binding(5) var tint_palette: texture_2d<f32>;
@group(1) @binding(6) var palette_sampler: sampler;
@group(1) @binding(7) var dirt_map: texture_2d<f32>;
@group(1) @binding(8) var diffuse_ramp: texture_2d<f32>;
@group(1) @binding(9) var diffuse_layer: texture_2d<f32>;
@group(1) @binding(10) var detail_map: texture_2d<f32>;
@group(1) @binding(11) var height_map: texture_2d<f32>;
// Hair: the noise map that breaks up its highlights and its alpha, and the _mask shaders' mask, on the second UV set.
@group(1) @binding(12) var hair_noise_map: texture_2d<f32>;
@group(1) @binding(13) var diffuse_mask: texture_2d<f32>;
// The _reflect shaders' environment: a sphere map, or a cube for the _cubemap_reflect ones.
@group(1) @binding(14) var environment_map: texture_2d<f32>;
@group(1) @binding(15) var environment_cube: texture_cube<f32>;

struct SurfaceInput {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec4f,
    @location(3) uv0: vec2f,
    @location(4) uv1: vec2f,
    @location(5) colour0: vec4f,
    @location(6) colour1: vec4f,
    @location(7) uv2: vec2f,
};

struct SurfaceOutput {
    @builtin(position) position: vec4f,
    // Relative to the scene's origin.
    @location(0) place: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec3f,
    @location(3) binormal: vec3f,
    @location(4) uv0: vec2f,
    @location(5) colour0: vec4f,
    // The tint palette's colour, white for untinted shaders.
    @location(6) tint: vec3f,
    // The second UV set: a vehicle's dirt is tiled over it.
    @location(7) uv1: vec2f,
    // Drawn into the reflection map: how far into the hemisphere being drawn the point is; behind it below zero.
    @location(8) hemisphere: f32,
    // The _pxm shaders' parallax controls: how much of the height to drop (x), and how flat a view may go before the
    // parallax stops growing (one less y).
    @location(9) controls: vec2f,
    // The interior room it's in (light.wgsl's use_room): nought outside, and in the viewer.
    @location(10) @interpolate(flat) room: vec4f,
    // Its placement's level of detail fade (light.wgsl's lod_fade_keeps): none in the viewer.
    @location(11) @interpolate(flat) lod_fade: vec2f,
    // The tint palette's alpha, which the trees' alpha takes.
    @location(12) tint_alpha: f32,
};

// The camera's reflection map being drawn (ViewportRenderer.Reflections.cs): the eye relative to the scene's origin,
// with the depth range in w, and the hemisphere, +1 upper or -1 lower, in x.
struct ReflectionView {
    eye: vec4f,
    side: vec4f,
};

@group(3) @binding(0) var<uniform> reflection_view: ReflectionView;

const TREE_UNPLACED = mat3x3f(vec3f(1.0, 0.0, 0.0), vec3f(0.0, 1.0, 0.0), vec3f(0.0, 0.0, 1.0));

@vertex
fn vs_surface(original: SurfaceInput) -> SurfaceOutput {
    let input = tree_input(original, TREE_UNPLACED);
    var output = surface_vertex(input);
    output.position = scene.view_projection * vec4f(input.position, 1.0) * day_night_vertex_scale();
    return output;
}

// A _geomnightonly shader's vertices all go to nought by day, as its vertex programs scale them (one while the day/night
// balance, globalScalars2.y, is above nought, else nought), so it draws nothing; any other's stay where they are.
fn day_night_vertex_scale() -> f32 {
    return select(1.0, select(0.0, 1.0, light.reflection.y > 0.0), material.environment.w > 0.5);
}

// A drawable into the reflection map, as GTA draws its world into its own: a paraboloid projection of the
// hemisphere around the eye, into that hemisphere's half of the map (light.wgsl's paraboloid_uv, within the half's
// viewport), with the distance as depth.
@vertex
fn vs_surface_paraboloid(original: SurfaceInput) -> SurfaceOutput {
    let input = tree_input(original, TREE_UNPLACED);
    var output = surface_vertex(input);
    let offset = input.position - reflection_view.eye.xyz;
    let distance = max(length(offset), 1e-5);
    let d = offset / distance;
    let side = reflection_view.side.x;
    let z = d.z * side;
    let p = d.xy / max(1.0 + z, 1e-3);
    output.position = vec4f(p.x * side, p.y, saturate(distance / reflection_view.eye.w), 1.0) * day_night_vertex_scale();
    output.hemisphere = z;
    return output;
}

fn surface_vertex(input: SurfaceInput) -> SurfaceOutput {
    var output: SurfaceOutput;
    output.hemisphere = 1.0;
    output.room = vec4f(0.0);
    output.place = input.position;
    // As VS_Transform: the normal normalised, the tangent as it is, the binormal cross(T, N) by the handedness.
    output.normal = normalize(input.normal);
    output.tangent = input.tangent.xyz;
    output.binormal = cross(input.tangent.xyz, output.normal) * input.tangent.w;
    output.uv0 = input.uv0;
    output.uv1 = input.uv1;
    output.controls = input.uv2;
    output.colour0 = input.colour0;
    // As VS_TransformD in the _tnt shaders: the palette read at the vertex blue's column, the selector's row.
    output.tint = vec3f(1.0);
    output.tint_alpha = 1.0;
    if (material.extra.y > 0.5) {
        let tint = textureSampleLevel(tint_palette, palette_sampler, vec2f(tint_column(input), material.extra.z), 0.0);
        output.tint = tint.rgb;
        output.tint_alpha = tint.a;
    }
    return output;
}

// What a family's deferred pixel shader writes, before lighting: albedo as stored (gamma 2), normal, specular
// intensity, power and fresnel, the natural ambient scale as stored (the lighting squares it), the reflection's
// scale, and alpha.
struct Surface {
    base: vec3f,
    normal: vec3f,
    intensity: f32,
    power: f32,
    fresnel: f32,
    ambient: f32,
    artificial: f32,
    reflection: f32,
    alpha: f32,
};

// What the HDR pass's surfaces write: the colour, and the sub-sampled alpha the game's G-buffer keeps in its first
// target's alpha, which its combine reads (subsample_alpha.wgsl): nought but on a cut-out's sub-sampled pixels.
struct SurfaceTargets {
    @location(0) colour: vec4f,
    @location(1) sub_sampled: f32,
    // And what the world's ambient occlusion takes back out (light.wgsl's occludable).
    @location(2) ambient: vec4f,
    @location(3) normal: vec4f,
};

// The sub-sampled alpha a cut-out leaves (shade_surface), nought for everything else.
var<private> sub_sampled_alpha: f32 = 0.0;

@fragment
fn fs_surface(input: SurfaceOutput, @builtin(front_facing) front: bool) -> SurfaceTargets {
    use_local_lights(input.position.xy, input.place);
    use_room(input.room);
    // Left out after shading, which needs uniform control flow.
    let colour = shade_surface(input, front, true);
    if (!lod_fade_keeps(input.position.xy, input.lod_fade)) {
        discard;
    }
    return SurfaceTargets(colour, sub_sampled_alpha, occludable_ambient, occludable_normal);
}

// Into the reflection map: the paraboloid mirrors the winding, and what is behind the hemisphere is left out (after
// shading, which needs uniform control flow).
@fragment
fn fs_surface_reflected(input: SurfaceOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    let colour = shade_surface(input, !front, false);
    if (input.hemisphere < 0.0) {
        discard;
    }
    return colour;
}

// In the view, where the cut-outs are sub-sampled, as the game's cut-out pass draws them on PC; elsewhere
// (the reflection map) they're clipped at their alpha reference, as its alpha-clip programs do.
fn shade_surface(original: SurfaceOutput, front: bool, in_view: bool) -> vec4f {
    // The _pxm shaders read their maps where the view meets the height map; the rest where the first UV set says.
    var input = original;
    let uv_dx = dpdx(original.uv0);
    let uv_dy = dpdy(original.uv0);
    input.uv0 = original.uv0 + surface_parallax(original, uv_dx, uv_dy);
    var self_shadow = 1.0;
    if (material.parallax.x > 0.5 && material.parallax.w > 0.0) {
        self_shadow = parallax_self_shadow(input);
    }

    // Every map is sampled before anything is decided, as derivatives need uniform control flow.
    let diffuse = textureSample(diffuse_map, surface_sampler, input.uv0);
    let bump = textureSample(normal_map, surface_sampler, input.uv0);
    let specular_sample = textureSample(specular_map, surface_sampler,
        select(input.uv0, input.uv1, material.spec_place.x > 0.5) * material.spec_place.y);
    // A vehicle's dirt tiles closer as dirtLevelMod.z goes down.
    let dirt = textureSample(dirt_map, surface_sampler, input.uv1 * (2.0 - material.dirt_level.z));
    let layer = textureSample(diffuse_layer, surface_sampler, select(input.uv0, input.uv1, material.spec_place.w > 0.5));
    // The detail map, read at its tiling and at 3.17 times that.
    let fine = textureSample(detail_map, surface_sampler, input.uv0 * material.detail.zw);
    let finer = textureSample(detail_map, surface_sampler, input.uv0 * 3.17 * material.detail.zw);
    // Hair's noise, at each highlight's tiling, and its mask.
    let noise1 = textureSample(hair_noise_map, surface_sampler, input.uv0 * 0.5 * material.hair_noise.xy);
    let noise2 = textureSample(hair_noise_map, surface_sampler, input.uv0 * 0.5 * material.hair_noise.zw);
    let hair_mask = textureSample(diffuse_mask, surface_sampler, input.uv1);

    if (material.tree.x > 0.5) {
        return shade_tree(input, diffuse, bump, specular_sample);
    }

    let ped = u32(material.ped.x + 0.5);
    let mode = u32(material.options.z + 0.5);
    let alpha = select(diffuse.a, ped_alpha(input, diffuse, noise1, hair_mask), ped != 0u);
    if (mode == 1u) {
        if (ped != 0u) {
            // The ped programs' cut, at their own reference.
            if (alpha < material.extra.x) {
                discard;
            }
        } else if (in_view && material.extra.w < 0.5) {
            sub_sampled_alpha = sub_sample(input.position.xy, alpha);
        } else if (alpha <= material.extra.x) {
            // PS_DeferredVehicleTextured_SubSampleAlpha clips at the vehicles' reference and keeps no sub-sampled alpha;
            // the alpha-clip programs at the global one.
            discard;
        }
    }
    if (material.glass.x > 0.5) {
        return glass(input, diffuse, specular_sample, dirt);
    }

    // PS_DeferredTextured's detail: the two reads' red and green, centred and summed; by detailSettings and the mask,
    // red darkens the diffuse and the specular, and both nudge the bump map before it is unpacked.
    var detail_scale = 1.0;
    var detail_bump = vec2f(0.0);
    if (material.detail_options.x > 0.5) {
        let detail = fine.xy - 1.0 + finer.xy;
        let mask_source = u32(material.detail_options.y + 0.5);
        var mask = 1.0;
        if (mask_source == 1u) {
            mask = specular_sample.a;
        } else if (mask_source == 2u) {
            mask = diffuse.a;
        }
        detail_scale = 1.0 - material.detail.x * mask * detail.x;
        detail_bump = material.detail.y * mask * detail;
    }

    // The surface normal: the bump map's XY scaled by bumpiness (at least 0.001) in the tangent frame, Z rebuilt.
    var normal = normalize(input.normal);
    if (material.options.x > 0.5) {
        let xy = (bump.xy + detail_bump) * 2.0 - 1.0;
        let z = sqrt(abs(1.0 - dot(xy, xy)));
        let scale = max(0.001, material.specular.w);
        normal = normalize(scale * xy.x * input.tangent + scale * xy.y * input.binormal + z * input.normal);
    }
    // The back of a surface drawn from both sides faces the other way.
    normal = select(-normal, normal, front);

    let view = normalize(scene.eye.xyz - (input.place + scene.origin.xyz));
    let to_light = normalize(light.sun_direction.xyz);
    var surface: Surface;
    if (ped != 0u) {
        surface = ped_surface(input, ped, diffuse, specular_sample, noise1, noise2, normal, view);
        surface.alpha = alpha;
    } else if (material.extra.w > 0.5) {
        surface = vehicle_surface(input, diffuse, specular_sample, dirt, layer, normal, view, to_light);
    } else {
        surface = textured_surface(input, diffuse, specular_sample, normal);
        surface.base *= detail_scale;
        // The _texdecal and _vertdecal shaders' decal over the tinted, detailed diffuse, itself untinted, as Enhanced's
        // PS_DeferredTextured has it: by the decal's own alpha, or by one less the vertex colour's.
        let decal = u32(material.spec_place.z + 0.5);
        if (decal != 0u) {
            let weight = select(layer.a, 1.0 - input.colour0.a, decal == 2u);
            surface.base = surface.base * (1.0 - weight) + layer.rgb * weight;
        }
        surface.intensity *= detail_scale;
        // The _reflect shaders' environment, added to the diffuse the G-buffer stores (and so kept within one).
        if (material.environment.x > 0.5) {
            surface.base = saturate(surface.base + environment_reflection(normal, view));
        }
    }
    surface.alpha = select(1.0, surface.alpha, mode == 2u);
    // With the parallax self-shadow, which the game's directional pass multiplies the sun by.
    let shadow = sun_shadow(input.place, select(-1.0, 1.0, front) * normalize(input.normal)) * self_shadow;
    return vec4f(lit(input, surface, diffuse, view, to_light, shadow), surface.alpha);
}

// PS_DeferredTextured_SubSampleAlpha's cut, checked against Enhanced's: the alpha hardened by HardAlphaBlend towards a
// ramp a tenth of the way from the reference to whole; on the checkerboard's "opaque" pixels only what is whole is
// kept, on the others all but nought. What it returns is what the game writes to its G-buffer's alpha for the combine:
// the alpha in sevenths, flagged as sub-sampled.
fn sub_sample(pixel: vec2f, alpha: f32) -> f32 {
    let reference = material.extra.x;
    let hard = (alpha - reference) / ((1.0 - reference) * 0.1);
    let blended = mix(alpha, hard, material.parallax_global.z);
    if ((ssa_opaque_pixel(pixel) && blended < 1.0) || blended <= 1.0 / 255.0) {
        discard;
    }
    return f32(u32(saturate(blended) * 7.0) | 8u) / 255.0;
}

// The game's SSAIsOpaquePixelFrac: the half of a checkerboard over the frame's pixels that a sub-sampled cut-out
// leaves to what is behind, unless it's whole.
fn ssa_opaque_pixel(pixel: vec2f) -> bool {
    return fract(dot(pixel, vec2f(0.5))) < 0.5;
}

// PS_VehicleTextured, as vehicle_vehglass compiles it (in its program without forward lights, PS_VehicleTextured_Zero):
// glass, lit as it is drawn and blended over what is behind it by its alpha.
//   - Colour: the diffuse map tinted towards matDiffuseColorTint by the vertex alpha, by matDiffuseColor and the vertex
//     red, squared into linear; the tint's alpha, by the vertex alpha, adds to the map's alpha.
//   - Dirt: the dirt map's red, by dirtLevelMod.x, towards dirtColor, and the vertex blue towards the map's blue; it
//     makes the glass more opaque and its green less shiny. The viewer shows a vehicle clean, dirtLevelMod.x nought.
//   - Specular: the specular map's R and G squared and B as it is, by specMapIntMask and the vertex red; its alpha the
//     power (times 510), turned into the exponent as the deferred passes do. Its fresnel is fixed at 0.96.
//   - The sun: the directional light with its shadow, by n.l on the side facing it, and through the glass, by as much as
//     it lets through, on the other; normalised Blinn-Phong for the highlight.
//   - Ambient and reflection: the natural and directional ambient on what the fresnel leaves, and the reflection map in
//     the mirrored view at a blur set by the exponent, by what the fresnel takes: as gta_lit's, by the vertex red squared.
// _crack is undamaged (its crack map unread) and turns its normal to face the camera. _inner reflects a twentieth as
// much (power times 512), its fresnel without the 0.04 floor, and it turns opaque at grazing angles.
fn glass(input: SurfaceOutput, diffuse: vec4f, specular_sample: vec4f, dirt: vec4f) -> vec4f {
    let program = u32(material.glass.x + 0.5);
    let view = normalize(scene.eye.xyz - (input.place + scene.origin.xyz));
    // The vertex normal as it is: the game culls the glass's back. _crack turns it to the camera.
    var normal = normalize(input.normal);
    if (program == 2u) {
        normal = select(-normal, normal, dot(normal, view) >= 0.0);
    }
    let ao = input.colour0.r;

    let tint = material.glass_tint;
    var albedo = diffuse.rgb * (1.0 + input.colour0.a * (tint.rgb - 1.0)) * material.paint.rgb * ao;
    var alpha = saturate(tint.w * input.colour0.a + diffuse.a);
    albedo = albedo * albedo;

    // Dirt, dry: its amount the map's red (wet, it leans to the blue).
    let level = material.dirt_level;
    let clean = 1.0 - level.z;
    let spread = clean * (input.colour0.b * clean - 1.0) + 1.0;
    let amount = dirt.r * level.x;
    albedo = mix(albedo, material.dirt_colour.rgb * level.y, amount * spread);
    albedo = mix(albedo, vec3f(dirt.b), clean * input.colour0.b * level.x);
    alpha += amount * spread;

    let squared = specular_sample.rg * specular_sample.rg;
    var intensity = dot(vec3f(squared, specular_sample.b), material.mask.xyz) * ao;
    var power = specular_sample.a * 510.0;
    if (program == 3u) {
        intensity *= 0.05;
        power = specular_sample.a * 512.0;
    }
    intensity = saturate(intensity * (1.0 - dirt.g * level.x * spread));
    let exponent = 3.0 * min(power, 500.0) + 558.0 * max(0.0, power - 500.0);

    let to_light = normalize(light.sun_direction.xyz);
    let n_dot_l = dot(normal, to_light);
    // Lit through from behind by as much as it lets through.
    let facing = abs(n_dot_l) + alpha * (saturate(n_dot_l) - abs(n_dot_l));
    let half_vector = normalize(view + to_light);
    let grazing = pow(1.0 - saturate(dot(normal, view)), 5.0);
    let grazing_half = pow(1.0 - saturate(dot(half_vector, to_light)), 5.0);
    var reflected = 0.04 + 0.96 * grazing;
    var highlight_fresnel = 0.04 + 0.96 * grazing_half;
    var shown = alpha;
    if (program == 3u) {
        shown = max(alpha, grazing);
        reflected = 1.0 - shown * (1.0 - grazing);
        highlight_fresnel = grazing_half;
    }
    let kept = 1.0 - intensity * reflected;
    let highlight = (exponent + 2.0) / 8.0 * pow(saturate(dot(normal, half_vector)), exponent) * highlight_fresnel * intensity;
    // Shadowed as a face turned to the sun, so light let through from behind isn't cut off at the terminator.
    let shadow = sun_shadow(input.place, select(-normal, normal, n_dot_l >= 0.0));
    let sun = light.sun_colour.rgb * shadow * facing * (albedo * kept + vec3f(highlight));

    let ambient_scale = ao * ao;
    let ambient = albedo * kept * gta_ambient(normal) * ambient_scale;
    let reflection = gta_reflection(normal, view, exponent, 1.0 - kept) * ambient_scale;
    return vec4f(sun + ambient + reflection, saturate(shown));
}

// PS_DeferredTextured. Specular intensity, power and fresnel as written to the G-buffer: a specular map's R and G
// squared and B as it is, weighed by specMapIntMask, and its alpha scaling the falloff.
fn textured_surface(input: SurfaceOutput, diffuse: vec4f, specular_sample: vec4f, normal: vec3f) -> Surface {
    var surface: Surface;
    surface.normal = normal;
    surface.intensity = material.specular.z;
    surface.power = material.specular.y;
    if (material.options.y > 0.5) {
        let weighed = vec3f(specular_sample.r * specular_sample.r, specular_sample.g * specular_sample.g, specular_sample.b);
        surface.intensity = material.specular.z * dot(weighed, material.mask.xyz);
        surface.power = material.specular.y * specular_sample.a;
    }
    let source = u32(material.spec_source.x + 0.5);
    if (source == 1u) {
        // emissive_speclum: the darker the diffuse map, the stronger and tighter the specular.
        let dark = 1.0 - dot(diffuse.rgb, vec3f(0.2125, 0.7154, 0.0721));
        surface.intensity = material.specular.z * dark;
        surface.power = material.specular.y * dark;
    } else if (source == 2u) {
        // diffspec: the diffuse colour's weighted sums, each overlaid by a strength, masked by specMapIntMask; these
        // shaders write no power (only the game's wetness adds some).
        let a = overlay(dot(diffuse.rgb, material.desaturate_intensity.xyz), material.desaturate_intensity.w);
        let b = overlay(dot(diffuse.rgb, material.desaturate_exponent.xyz), material.desaturate_exponent.w);
        surface.intensity = material.specular.z * dot(vec3f(a, b, 0.0), material.mask.xyz);
        surface.power = 0.0;
    }
    surface.fresnel = material.specular.x;
    // Tinted shaders multiply the diffuse map by the palette's colour.
    surface.base = diffuse.rgb * input.tint;
    // The vertex colour's red is the natural ambient (its green the artificial, which the viewer leaves out).
    surface.ambient = occluded(input.colour0.r, light.natural_up.w);
    surface.artificial = input.colour0.g;
    surface.reflection = 1.0;
    surface.alpha = diffuse.a * select(1.0, input.colour0.a, material.parallax_global.y > 0.5);
    // A night-only emissive decal fades with its emissive.
    if (material.parallax_global.y > 0.5 && material.environment.z > 0.5) {
        surface.alpha *= light.reflection.y;
    }
    return surface;
}

// The _reflect shaders' environment, as their programs compute it: the view mirrored about the surface normal, read
// from a sphere map at its X and Z, each one more and halved the other way (the sampler wraps them), or from a cube
// along it with Y and Z swapped; by reflectivePower.
fn environment_reflection(normal: vec3f, view: vec3f) -> vec3f {
    let r = normalize(reflect(-view, normal));
    var colour: vec3f;
    if (material.environment.x > 1.5) {
        colour = textureSample(environment_cube, surface_sampler, r.xzy).rgb;
    } else {
        colour = textureSample(environment_map, surface_sampler, (r.xz + 1.0) * -0.5).rgb;
    }
    return colour * material.environment.y;
}

// PS_DeferredTextured's parallax occlusion mapping, as the _pxm programs compile it: the height map marched along
// the view in tangent space, in 27 - 24|N.V| steps (more the flatter the view), with the first UV set's gradients;
// the march stops at the first step under the heights and meets them between that step and the last. Returns the
// offset to the first UV set.
fn surface_parallax(input: SurfaceOutput, uv_dx: vec2f, uv_dy: vec2f) -> vec2f {
    if (material.parallax.x < 0.5) {
        return vec2f(0.0);
    }
    let flattest = clamp(1.0 - input.controls.y, 0.1, 1.0);
    let keep = 1.0 - saturate(input.controls.x);
    let v = scene.eye.xyz - (input.place + scene.origin.xyz);
    let tangent_view = normalize(vec3f(dot(input.tangent, v), dot(input.binormal, v), dot(input.normal, v)));
    let lean = max(flattest, tangent_view.z);
    let n_dot_v = abs(dot(normalize(v), normalize(input.normal)));
    let amount = material.parallax_global.x * keep * saturate(26.0 - 24.0 * n_dot_v) * saturate(n_dot_v * 4.0);
    let reach = -tangent_view.xy / lean * amount * material.parallax.y;
    let start = tangent_view.xy / lean * amount * material.parallax.z;
    let steps = i32(27.0 - 24.0 * n_dot_v);
    var t = 0.0;
    if (steps != 0) {
        let step = 1.0 / f32(steps);
        let advance = reach * step;
        let h0 = textureSampleGrad(height_map, surface_sampler, input.uv0, uv_dx, uv_dy).x + 1e-6;
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
            h = textureSampleGrad(height_map, surface_sampler, input.uv0 + offset, uv_dx, uv_dy).x;
        }
        let after = layer - h;
        let before = previous_layer - previous_h;
        t = h;
        if (before - after > 0.0) {
            t = (before * layer - after * previous_layer) / (before - after);
        }
        t = clamp(t, 0.0, 1.0);
    }
    return (1.0 - t) * reach + start;
}

// The _pxm programs' self-shadow: the height map read towards the sun in tangent space at seven steps, each step's
// rise over the line to the sun weighed more the further it is; the highest kept, by parallaxSelfShadowAmount.
fn parallax_self_shadow(input: SurfaceOutput) -> f32 {
    let travel = -light.sun_direction.xyz;
    let towards = vec3f(dot(input.tangent, travel), dot(input.binormal, travel), dot(input.normal, travel));
    let along = towards.xy * (1.0 - input.controls.x) * material.parallax.y * material.parallax_global.x / towards.z;
    let h0 = textureSample(height_map, surface_sampler, input.uv0).x;
    var steps = array<f32, 7>(0.88, 0.77, 0.66, 0.55, 0.44, 0.33, 0.22);
    var weights = array<f32, 7>(1.0, 2.0, 4.0, 6.0, 8.0, 10.0, 12.0);
    var highest = -1e9;
    for (var k = 0; k < 7; k++) {
        let h = textureSample(height_map, surface_sampler, input.uv0 + along * steps[k]).x;
        highest = max(highest, (h - h0 - steps[k]) * weights[k]);
    }
    return 1.0 - material.parallax.w * saturate(highest);
}

// The overlay blend of a value by a strength, as the diffspec shaders write it.
fn overlay(value: f32, strength: f32) -> f32 {
    return select(1.0 - 2.0 * (1.0 - value) * (1.0 - strength), 2.0 * value * strength, value < 0.5);
}

// PS_DeferredVehicleTextured, as vehicle_paint1 and vehicle_mesh compile it; the rest of the family is one of the two
// with fewer inputs. The vertex colour's red is ambient occlusion, for every term; its blue sets where the dirt level
// applies (the pixel shader's dirt and ramp terms); its green scales the damage deformation in the vertex shader
// (VS_VehicleTransformD), which an undamaged vehicle doesn't have.
fn vehicle_surface(input: SurfaceOutput, diffuse: vec4f, specular_sample: vec4f, dirt: vec4f, layer: vec4f, normal: vec3f, view: vec3f,
    to_light: vec3f) -> Surface {
    var surface: Surface;
    surface.normal = normal;
    let occlusion = input.colour0.r;
    let level = material.dirt_level;
    let n_dot_v = saturate(dot(normal, view));

    // The paint: its colour, or with a ramp the colour at this viewing angle, pulled towards the ramp's end by the dirt
    // level where the vertex blue lets it.
    var paint = material.paint.rgb;
    if (material.vehicle.z > 0.5 && material.paint.w > 0.0) {
        paint = textureSampleLevel(diffuse_ramp, surface_sampler, vec2f(clamp(n_dot_v, 0.002, 0.998), 0.0), 0.0).rgb;
        if (1.0 - level.z > 0.0) {
            let end = textureSampleLevel(diffuse_ramp, surface_sampler, vec2f(0.998, 0.0), 0.0).x;
            paint = mix(paint, vec3f(end), level.x * input.colour0.b * (1.0 - level.z));
        }
    }
    // The diffuse map in the paint's colour, then the second layer (a livery or decals) over it, by its alpha and
    // matDiffuseColor2's strength, in matDiffuseColor2's colour; the vertex AO over both.
    var base = diffuse.rgb * paint;
    if (material.spec_place.z > 0.5) {
        base = mix(base, material.layer.rgb * layer.rgb, material.layer.w * layer.a);
    }
    base *= occlusion;

    // Dirt: how much of it shows, by the dirt map, the level and the vertex blue; its colour over the paint, and the
    // map's own over that where it is dirtiest. Its green roughens the surface, taking the specular away.
    var roughness = 0.0;
    if (material.dirt_colour.w > 0.5) {
        let masked = (1.0 - level.z) * input.colour0.b;
        let spread = (1.0 - level.z) * (masked - 1.0) + 1.0;
        let amount = dirt.x * level.x * spread;
        roughness = dirt.y * level.x * spread;
        base = mix(base, level.y * material.dirt_colour.rgb, amount);
        base = mix(base, vec3f(dirt.z), masked * level.x);
    }

    // Specular, as the shader's own program writes it: the vertex AO times a scale, the specular map's part and the
    // roughness; the power from a scale and the map; fresnel a constant or the map's.
    let rr = specular_sample.r * specular_sample.r;
    let gg = specular_sample.g * specular_sample.g;
    let weighed = dot(vec3f(rr, gg, specular_sample.b), material.mask.xyz);
    let model = vec4u(material.spec_model + 0.5);
    var map = 1.0;
    if (model.x == 1u) {
        map = weighed;
    } else if (model.x == 2u) {
        map = rr;
    }
    var rough = 1.0;
    if (model.y == 1u) {
        rough = 1.0 - roughness;
    } else if (model.y == 2u) {
        rough = level.z;
    }
    surface.intensity = occlusion * material.specular.z * map * rough;
    var power = 1.0;
    if (model.z == 1u) {
        power = specular_sample.a;
    } else if (model.z == 2u) {
        power = gg;
    }
    surface.power = material.specular.y * power;
    surface.fresnel = select(material.specular.x, 1.0 - specular_sample.b, model.w == 1u);
    // The second lobe: paints weigh it by the masked map and take its exponent from the map's alpha; the others from
    // red and green.
    var lobe_strength: f32;
    var lobe_exponent: f32;
    if (material.vehicle.x > 0.5) {
        lobe_strength = occlusion * 0.75 * level.w * weighed;
        lobe_exponent = specular_sample.a * 15.0;
    } else {
        lobe_strength = 0.6 * occlusion * rr * level.w;
        lobe_exponent = gg * 20.0;
    }

    // The second lobe, a paint's sheen: lit from the sun swung towards overhead by DirLerp, and kept in the albedo.
    if (material.vehicle.y > 0.5) {
        let towards = normalize(mix(to_light, vec3f(0.0, 0.0, 1.0), material.specular2.w));
        let half_vector = normalize(view + towards);
        let sheen = pow(saturate(dot(normal, half_vector) + 1e-8), lobe_exponent + 1e-8);
        base = min(base + material.specular2.rgb * lobe_strength * sheen, vec3f(240.0));
    }

    surface.base = base;
    surface.ambient = occluded(occlusion, light.directional_ambient.w);
    surface.reflection = 1.0;
    surface.alpha = diffuse.a * input.colour0.a;
    return surface;
}

// The natural ambient scale a deferred shader stores, gLightArtificialIntAmbient0.w plus globalScalars.z times the
// vertex's occlusion: taken as one less the timecycle's occlusion multiplier for the kind of thing it is, plus the
// multiplier times the occlusion, so the multiplier is the occlusion's share.
fn occluded(occlusion: f32, multiplier: f32) -> f32 {
    return 1.0 - multiplier * (1.0 - saturate(occlusion));
}

// PS_DeferredPedTextured's alpha: the diffuse map's; the diffuse-only programs' by the vertex alpha; hair's by its
// noise and AnisotropicAlphaBias, and the _mask shaders' mask. The _palette shaders' alpha is the palette index, and
// solid where it is one.
fn ped_alpha(input: SurfaceOutput, diffuse: vec4f, noise: vec4f, mask: vec4f) -> f32 {
    let ped = u32(material.ped.x + 0.5);
    var alpha = diffuse.a;
    if (ped == 2u) {
        alpha *= input.colour0.a;
    } else if (ped == 3u) {
        alpha *= saturate(material.hair_colour.w + 2.0 * noise.w);
        if (material.ped.w > 0.5) {
            alpha *= mask.x;
        }
    }
    if (material.ped.z > 0.5) {
        let index = floor(diffuse.a * 255.01);
        if (index >= 32.0 && index <= 160.0) {
            alpha = 1.0;
        }
    }
    return alpha;
}

// PS_DeferredPedTextured at rest, as the ped programs compile it, without what the game sets as a ped lives: no
// damage, blood or tattoos, dry, its wrinkles relaxed, no snow, fat or sweat, its colour scales at one, the dynamic
// bake at the timecycle's default (0.5, 1). The vertex colour's red is ambient occlusion; its green weighs the rim
// light, bends the normal towards the view where it is darkest, and sets the reflection's scale.
fn ped_surface(input: SurfaceOutput, ped: u32, diffuse: vec4f, specular_sample: vec4f, noise1: vec4f, noise2: vec4f,
    bumped: vec3f, view: vec3f) -> Surface {
    var surface: Surface;
    let green = input.colour0.g;
    // The diffuse-only programs read neither the bump map nor the specular map: the vertex normal, on the side seen.
    let side = select(-1.0, 1.0, dot(bumped, input.normal) >= 0.0);
    let normal = select(bumped, normalize(input.normal) * side, ped == 2u);
    let n_dot_v = saturate(dot(view, normal));

    // The rim light: light_ped_rim_mult times the vertex green above 10, towards the silhouette and away from the
    // ground; hair's is broader. The diffuse-only programs and the forward ped_alpha have none.
    let rims = material.ped.y > 0.5 && ped != 2u;
    let rim_weight = select(0.0, saturate((green - 10.0 / 255.0) * (245.0 / 255.0) * light.ped.y), rims);
    let upward = saturate(0.5 * normal.z + 0.2) / 0.7;
    let t = saturate(2.5 * (0.5 - n_dot_v));
    var rim = t * t * (3.0 - 2.0 * t) * upward * rim_weight;
    if (ped == 3u) {
        rim = (0.5 + 0.4 * (1.0 - n_dot_v)) * upward * rim_weight;
    }
    surface.base = diffuse.rgb * (1.0 + rim);

    // Specular: the map's red squared by specularIntensityMult, its green squared by specularFalloffMult, and
    // specularFresnel; the diffuse-only programs write none, and a fresnel of 0.98.
    surface.intensity = specular_sample.r * specular_sample.r * material.specular.z;
    surface.power = specular_sample.g * specular_sample.g * material.specular.y;
    surface.fresnel = material.specular.x;
    if (ped == 2u) {
        surface.intensity = 0.0;
        surface.power = 0.0;
        surface.fresnel = 0.98;
    }

    // Skin and clothes: the normal bent towards the view by one less 25.5 times the vertex green, so only the darkest
    // greens bend it.
    surface.normal = normal;
    if (rims && ped == 1u) {
        surface.normal = normalize(mix(normal, view, saturate(1.0 - 25.5 * green)));
    }

    // Hair: two highlights along the strands (the binormal), each nudged off it by the noise, lit from the camera's
    // axis rather than the sun. Where the specular map's blue marks hair they scale the specular intensity, and the
    // second tints the albedo in anisotropicSpecularColour.
    if (ped == 3u) {
        let strand = normalize(input.binormal);
        let across = normalize(input.normal);
        let axis = -scene.forward.xyz;
        let s1 = kajiya(axis, view, normalize(strand + (0.2 * noise2.x - 0.1) * across));
        let s2 = kajiya(axis, view, normalize(strand + (0.2 * noise2.y - 0.1) * across));
        let second = pow(s2, material.hair.w + 16.0);
        let aniso = material.hair.x * pow(s1, material.hair.z + 8.0) + material.hair.y * second;
        if (specular_sample.b < 1.0 / 32.0) {
            surface.base += 0.5 * second * material.hair_colour.rgb;
            surface.intensity *= aniso * noise1.x;
        }
    }

    // The natural ambient: the ped occlusion multiplier's share of the vertex occlusion, times the dynamic bake, which
    // lights what faces up more (one and a half straight up, a half straight down).
    let bake = 0.5 + (0.5 * normal.z + 0.5);
    surface.ambient = 1.0 - light.ped.x + light.ped.x * saturate(input.colour0.r) * bake;

    // The reflection's scale: the vertex green plus 135, cubed, stored with the specular map's blue marking skin
    // (13 to 15 sixteenths) in the G-buffer's last channel, which the lighting decodes and squares.
    var scale = 1.0;
    if (ped == 1u) {
        let x = saturate(pow(green + 135.0 / 255.0, 3.0));
        let skin = abs(16.0 * specular_sample.b - 14.0) <= 1.0;
        scale = select(saturate(2.0 * ((x + 260.0 / 255.0) * 127.0 / 255.0 - 0.5)), saturate(2.0 * x * 127.0 / 255.0), skin);
    }
    surface.reflection = scale * scale;
    return surface;
}

// The hair programs' highlight for a strand's direction: the sine between the strand and the light times that between
// the strand and the view, less the cosines' product.
fn kajiya(towards: vec3f, view: vec3f, strand: vec3f) -> f32 {
    let l = dot(towards, strand);
    let v = dot(view, strand);
    return abs(sqrt(saturate(1.0 - l * l)) * sqrt(saturate(1.0 - v * v)) - l * v);
}

// The game's lighting passes (light.wgsl's gta_lit) over what a family wrote, with the ambient scale the family's
// deferred shader stores, which the ambient pass squares (the G-buffer holds up to two). Then emissive, which the game
// adds in its ambient pass.
fn lit(input: SurfaceOutput, surface: Surface, diffuse: vec4f, view: vec3f, to_light: vec3f, shadow: f32) -> vec3f {
    let scale = clamp(surface.ambient, 0.0, 2.0);
    let artificial = clamp(surface.artificial, 0.0, 2.0);
    let colour = gta_lit(surface.base, surface.normal, view, surface.intensity, surface.power, surface.fresnel, scale * scale, shadow,
        surface.reflection, artificial * artificial);

    // Emissive: the vertex colour's blue by the diffuse alpha and emissiveMultiplier, and the night-only shaders' by
    // the day/night balance, stored as a sixteenth and brought back, facing the camera.
    var glow = vec3f(0.0);
    if (material.options.w > 0.5) {
        let albedo = surface.base * surface.base;
        let night = select(1.0, light.reflection.y, material.environment.z > 0.5);
        let stored = saturate(input.colour0.b / 16.0 * diffuse.a * material.mask.w * night);
        glow = albedo * stored * 16.0 * saturate(dot(surface.normal, view));
    }
    return colour + glow;
}

// The game's tree shaders' pixel programs, then their light. The game draws its cut-outs with sub-sampled alpha (its
// default on PC, with no MSAA): the _SubSampleAlpha programs, checked against Enhanced's. What they write to the G-buffer:
//   - Coverage: the diffuse map's alpha ramped from nought at 0.1 to whole at 0.75, a pixel kept where it is above the
//     game's 3x3 dither (tree_dither), which its TAA then averages. So a far billboard, whose atlas's small mips have
//     blurred every cell's alpha to a little above nought, keeps only the few pixels its blur leaves above 0.1.
//   - Colour and alpha: the diffuse map, tinted by the palette (the foliage's tint takes the palette's alpha too); the
//     alpha (the foliage's ramped as above) AlphaScale times the map's (times the tint's), doubled, and cut out where it
//     is no more than AlphaTest.
//   - The normal: the normal map's in the vertex frame (the foliage's with one), else the vertex's, pulled up towards Z
//     by one less UseTreeNormals; the billboards' as their vertex shader left it.
//   - Specular: the specular map's mask by specularIntensityMult and its alpha by specularFalloffMult, the intensity
//     stored four times over (so only a quarter of it fits) and read back squared; specularFresnel. The _lod shaders write
//     none, the billboards an exponent of 512 and a fresnel of 0.4999.
//   - SelfShadowing (one for the billboards), which the light multiplies the shadow by; the ambient scales, the foliage's
//     from the vertex alpha (how deep in the tree), the others' from the vertex red and green; ShadowFalloff.
// The billboards are lit as any other surface. The rest are lit by the game's foliage pass, PS_directional_scatter: the
// normal turned to face the sun; with ShadowFalloff above nought, the specular, its exponent and the reflection scaled
// by the shadow, and the light through the leaves added (the scatter): the shadow map's depth under the first caster by
// ShadowFalloff and 100 squared, s; then max(0.49 - N.L, 0) times the ambient by 0.14 plus exp(-s^2), in the sun's colour,
// on the albedo. Without it (PS_directional_backlit), what faces away is lit in the sun's colour tinted a quarter of the
// way to white from the albedo (trees store a skin blend of one), the ambient and reflection by the normal as it was.
fn shade_tree(input: SurfaceOutput, diffuse: vec4f, bump: vec4f, specular_sample: vec4f) -> vec4f {
    let program = u32(material.tree.x + 0.5);
    if (tree_dither(input.position.xy) > saturate((diffuse.a - 0.1) / 0.65)) {
        discard;
    }
    var colour = diffuse;
    // The foliage's alpha test takes the ramped alpha, a 255th over.
    if (program == 1u) {
        colour.a = saturate((diffuse.a - 0.1) / 0.65 + 1.0 / 255.0);
    }
    var alpha_tint = 1.0;
    if (material.extra.y > 0.5 && program != 3u) {
        alpha_tint = input.tint_alpha;
        if (program == 1u) {
            colour *= vec4f(input.tint, input.tint_alpha);
        } else {
            colour = vec4f(colour.rgb * input.tint, colour.a);
        }
    }
    let alpha = 2.0 * material.tree.z * colour.a * alpha_tint;
    if (alpha <= material.tree.w) {
        discard;
    }

    let up = vec3f(0.0, 0.0, 1.0);
    var normal = input.normal;
    if (program == 1u && material.options.x > 0.5) {
        let xy = bump.xy * 2.0 - 1.0;
        let z = sqrt(abs(1.0 - dot(xy, xy)));
        let scale = max(0.001, material.specular.w);
        normal = normalize(scale * xy.x * input.tangent + scale * xy.y * input.binormal + z * input.normal);
    }
    if (program != 3u) {
        normal = mix(up, normal, material.tree_shade.x);
    }
    normal = normalize(normal);

    var level = material.specular.z;
    var power = material.specular.y;
    if (material.options.y > 0.5) {
        level *= dot(specular_sample.rgb, material.mask.xyz);
        power *= specular_sample.a;
    }
    let stored = saturate(4.0 * level);
    let intensity = stored * stored;
    power = min(power, 512.0);
    let fresnel_value = material.specular.x;

    var natural = input.colour0.r;
    var artificial = input.colour0.g;
    if (program == 1u) {
        natural = input.colour0.a;
        artificial = input.colour0.a;
    }
    let ambient = clamp(occluded(natural, light.natural_up.w), 0.0, 2.0);
    let artificial_scale = clamp(artificial, 0.0, 2.0);
    let view = normalize(scene.eye.xyz - (input.place + scene.origin.xyz));
    // ShadowFalloff as the G-buffer holds it, beside a skin blend of nought.
    let falloff = material.tree_shade.z * (1.0 - 2.0 / 255.0);
    if (program == 3u) {
        let shadow = sun_shadow(input.place, normal) * material.tree_shade.y;
        return vec4f(gta_lit(colour.rgb, normal, view, intensity, power, fresnel_value, ambient * ambient, shadow, falloff,
            artificial_scale * artificial_scale), 1.0);
    }
    return vec4f(tree_foliage_light(input.place, colour.rgb, normal, view, intensity, power, fresnel_value, ambient * ambient,
        artificial_scale * artificial_scale, falloff), 1.0);
}

// The game's foliage light (PS_directional_scatter, or PS_directional_backlit without a falloff) over a tree's G-buffer:
// gta_lit's directional and ambient arithmetic with the normal turned to the sun, as above.
fn tree_foliage_light(place: vec3f, stored: vec3f, original: vec3f, view: vec3f, intensity: f32, power: f32, fresnel_value: f32,
    ambient_scale: f32, artificial_scale: f32, falloff: f32) -> vec3f {
    let to_light = normalize(light.sun_direction.xyz);
    let behind = dot(original, to_light) < 0.0;
    let normal = select(original, -original, behind);
    let scatter = falloff > 0.0;
    let shadow = sun_shadow(place, normal) * material.tree_shade.y;
    let albedo = stored * stored;
    var spec = saturate(intensity);
    var p = clamp(power, 0.0, 512.0);
    var reflection_scale = falloff;
    var sun_colour = light.sun_colour.rgb;
    // The ambient and reflection's normal: the turned one when scattering, the original when back-lit.
    var around = normal;
    if (scatter) {
        spec *= shadow;
        p *= shadow;
        reflection_scale *= shadow;
    } else {
        if (behind) {
            sun_colour *= mix(albedo, vec3f(1.0), 0.25);
        }
        around = original;
    }
    let exponent = 3.0 * min(p, 500.0) + 558.0 * max(0.0, p - 500.0);
    let half_vector = normalize(to_light + view);
    let n_dot_l = saturate(dot(normal, to_light));
    let h_dot_l = saturate(dot(half_vector, to_light));
    let n_dot_h = saturate(dot(normal, half_vector) + 1e-8);
    let kept = 1.0 - fresnel(fresnel_value, saturate(dot(normal, view))) * spec;
    let highlight = (exponent + 2.0) / 8.0 * pow(n_dot_h, exponent + 1e-8) * fresnel(fresnel_value, h_dot_l);
    let sun = sun_colour * shadow * (albedo * kept * n_dot_l + vec3f(n_dot_l * spec * highlight));
    let ambient = albedo * kept * (gta_ambient(around) * ambient_scale + gta_artificial_ambient(around) * artificial_scale);
    occludable(ambient, 1.0, around, albedo);
    let reflection = gta_reflection(around, view, exponent, 1.0 - kept) * ambient_scale * reflection_scale;
    let local = gta_local_lights(albedo, kept, spec, exponent, fresnel_value, normal, view);
    var through = vec3f(0.0);
    if (scatter) {
        let s = falloff * 10000.0 * tree_scatter_depth(place, normal);
        let amount = max(0.49 - dot(normal, to_light), 0.0);
        through = amount * (ambient * 0.14 + vec3f(exp(-s * s))) * light.sun_colour.rgb * albedo;
    }
    return sun + ambient + reflection + local + through;
}
