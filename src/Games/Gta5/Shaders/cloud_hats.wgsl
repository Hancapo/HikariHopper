// GTA V's cloud hats, as the game draws them (the clouds effects' vertex and
// pixel programs, unlit, scattering and piercing per vertex, the same in Enhanced): the big clouds round the horizon and
// overhead, each layer of the weather's hat one of the game's models, drawn after the world's water and before its
// other blended surfaces, as the game draws them after its sky. The constants are those the game
// sets from the weather's cloud keyframes and the timecycle (GtaCloudHats.cs); the fog is the sky's (Gta5/sky.wgsl).
//   - Each geometry's effect: three animated density layers (clouds_anim, clouds_animsoft, clouds_altitude) or one
//     (clouds_fast, clouds_soft); faded against the scene's depth (the soft ones, which the world hides) or at the far
//     plane, where only the sky is (clouds_fast, clouds_anim, which the game also keeps to the sky with its stencil);
//     clouds_altitude's layers slide against the camera instead.
//   - Alpha blended over the scene; the target's alpha (the sky's cover) is left as it is.
//   - Under the water's level they're cut away, as the game's PS3 path does; elsewhere the game's water, which writes
//     its depth, hides them there, and the viewer's water doesn't write it.
// Put after scene.wgsl, Gta5/sky_system.wgsl and Gta5/sky.wgsl.

struct CloudHatLayer {
    // gSkyColor, gEastMinusWestColor and gWestColor: the fill from the sky, east less west, and west.
    sky_colour: vec4f,
    east_minus_west: vec4f,
    west_colour: vec4f,
    // gSunDirection, gSunColor, gCloudColor, gAmbientColor, gBounceColor.
    sun_direction: vec4f,
    sun_colour: vec4f,
    cloud_colour: vec4f,
    ambient_colour: vec4f,
    bounce_colour: vec4f,
    // gDensityShiftScale: the density's shift and scale, whether there's a depth buffer.
    density_shift_scale: vec4f,
    // gScatterG_GSquared_PhaseMult_Scale.
    scatter: vec4f,
    // gPiercingLightPower_Strength_NormalStrength_Thickness.
    piercing: vec4f,
    // gScaleDiffuseFillAmbient, gWrapLighting_MSAARef, gNearFarQMult.
    scale_diffuse_fill_ambient: vec4f,
    wrap_lighting: vec4f,
    near_far_q: vec4f,
    // gAnimCombine, gAnimSculpt, gAnimBlendWeights: each texture layer's mode and weight.
    anim_combine: vec4f,
    anim_sculpt: vec4f,
    anim_blend_weights: vec4f,
    // gUVOffset: the three texture layers' animated offsets; the second's last two, the altitude layers' scroll.
    uv_offset: array<vec4f, 2>,
    // gCameraPos, with the inverted depth flag in w.
    camera: vec4f,
    // The material's values: gUVOffset1 and 2; gUVOffset3 and gRescaleUV1; gRescaleUV2 and 3; gSoftParticleRange,
    // gEnvMapAlphaScale and cloudLayerAnimScale1; cloudLayerAnimScale2 and 3.
    uv_offset12: vec4f,
    uv_offset3_rescale1: vec4f,
    rescale23: vec4f,
    soft_range: vec4f,
    anim_scale23: vec4f,
    // gWorld: the model's axes and its place, relative to the camera.
    world: array<vec4f, 4>,
    // The effect: animated, soft, infinite, camera scroll (one or nought each).
    effect: vec4f,
    // The water's level.
    water: vec4f,
};

@group(2) @binding(0) var<uniform> hat: CloudHatLayer;
// DensitySampler and NormalSampler wrap across and clamp along (wrapping both for clouds_altitude); the detail
// layers wrap both ways.
@group(2) @binding(1) var hat_sampler: sampler;
@group(2) @binding(2) var hat_detail_sampler: sampler;
@group(2) @binding(3) var density_map: texture_2d<f32>;
@group(2) @binding(4) var normal_map: texture_2d<f32>;
@group(2) @binding(5) var detail_density_map: texture_2d<f32>;
@group(2) @binding(6) var detail_normal_map: texture_2d<f32>;
@group(2) @binding(7) var detail_density2_map: texture_2d<f32>;
@group(2) @binding(8) var detail_normal2_map: texture_2d<f32>;
// DepthMapTexture: the scene's depth before the blended surfaces.
@group(3) @binding(0) var hat_depth: texture_depth_2d;

struct CloudHatIn {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    // Alpha the cloud's alpha, red how much haze it takes, green its density's combine scale, blue its sculpt adjust.
    @location(2) colour: vec4f,
    @location(3) uv: vec2f,
    @location(4) tangent: vec4f,
};

struct CloudHatVarying {
    @builtin(position) position: vec4f,
    // The eye ray, made unit, and its length.
    @location(0) eye_ray: vec4f,
    // The normal, tangent and binormal in the world, with the cloud's alpha, density combine scale and sculpt adjust.
    @location(1) normal: vec4f,
    @location(2) tangent: vec4f,
    @location(3) binormal: vec4f,
    @location(4) uv: vec2f,
    @location(5) uv23: vec4f,
    // The view depth (ScreenPos.w).
    @location(6) view_depth: f32,
    @location(7) piercing: vec4f,
    @location(8) scattering: vec3f,
    // The fog and haze: the colour added, and what's left of the cloud's.
    @location(9) fog: vec4f,
};

// The cloud's fog: the sky's fog along the ray, at least as much as the cloud's haze, then the haze at the far
// plane over it, as one colour added and one scale.
fn cloud_fog(eye_ray: vec3f, haze_alpha: f32) -> vec4f {
    var fog = gta_fog_no_haze(sky.fog, eye_ray);
    fog.w = max(fog.w, haze_alpha);
    let haze = haze_alpha * sky.fog.params[2].w;
    let haze_colour = vec3f(sky.fog.sun.w, sky.fog.atmosphere.w, sky.fog.ground.w);
    return vec4f(fog.rgb * fog.a * (1.0 - haze) + haze_colour * haze, (1.0 - fog.w) * (1.0 - haze));
}

// Mie scattering: Henyey-Greenstein's phase.
fn cloud_mie(view: vec3f, light: vec3f) -> f32 {
    let cos_theta = dot(view, light);
    let phase = (1.0 + cos_theta * cos_theta) / pow(abs(1.0 + hat.scatter.y - 2.0 * hat.scatter.x * cos_theta), 1.5);
    return phase * hat.scatter.z;
}

// The light partly piercing the cloud.
fn cloud_piercing_partial(view: vec3f) -> vec4f {
    let light = hat.sun_direction.xyz;
    let w = pow(saturate(dot(view, light)), hat.piercing.x);
    let p = -view;
    return vec4f(normalize(p - dot(p, light) * light), w);
}

fn cloud_world(v: vec3f) -> vec3f {
    return v.x * hat.world[0].xyz + v.y * hat.world[1].xyz + v.z * hat.world[2].xyz;
}

// Its vertex program (pulled in from the far plane) or its infinite variant (at it).
@vertex
fn vs_gta_cloud_hat(in: CloudHatIn) -> CloudHatVarying {
    var out: CloudHatVarying;
    // The world matrix is relative to the camera, and the view's origin is the camera.
    let eye = cloud_world(in.position) + hat.world[3].xyz;
    let length_eye = length(eye);
    var clip = scene.view_projection * vec4f(eye, 1.0);
    let inverted = hat.camera.w != 0.0;
    if (hat.effect.z != 0.0) {
        clip.z = select(clip.w, 0.0, inverted);
    } else if (clip.w > 0.0) {
        if (inverted) {
            if (clip.z < 0.0) {
                clip.z = 0.1 / clip.w;
            }
        } else if (clip.z > clip.w) {
            clip.z = clip.w - 0.1;
        }
    }
    out.position = clip;
    out.view_depth = clip.w;
    out.fog = cloud_fog(eye, 1.0 - in.colour.r);
    let view = eye / length_eye;
    out.eye_ray = vec4f(view, length_eye);

    let binormal = cross(in.tangent.xyz, in.normal) * in.tangent.w;
    out.normal = vec4f(normalize(cloud_world(in.normal)), in.colour.a);
    out.tangent = vec4f(normalize(cloud_world(in.tangent.xyz)), in.colour.g);
    out.binormal = vec4f(normalize(cloud_world(binormal)), in.colour.b);

    let rescale1 = hat.uv_offset3_rescale1.zw;
    let rescale2 = hat.rescale23.xy;
    let rescale3 = hat.rescale23.zw;
    if (hat.effect.w != 0.0) {
        // With the camera's UV scroll: every layer slides against the camera (the first's saturate keeps gUVOffset[0] in the
        // buffer and is nought).
        let scroll = hat.uv_offset[1].zw;
        out.uv = in.uv * rescale1 + hat.uv_offset12.xy + scroll + saturate(hat.uv_offset[0].xy - 1000.0);
        out.uv23 = vec4f(in.uv * rescale2 + hat.uv_offset12.zw + scroll, in.uv * rescale3 + hat.uv_offset3_rescale1.xy + scroll);
    } else {
        out.uv = in.uv * rescale1 + hat.uv_offset12.xy + hat.uv_offset[0].xy * hat.soft_range.zw;
        out.uv23 = vec4f(in.uv * rescale2 + hat.uv_offset12.zw + hat.uv_offset[0].zw * hat.anim_scale23.xy,
            in.uv * rescale3 + hat.uv_offset3_rescale1.xy + hat.uv_offset[1].xy * hat.anim_scale23.zw);
    }

    out.scattering = cloud_mie(view, hat.sun_direction.xyz) * hat.sun_colour.rgb * hat.scatter.w;
    out.piercing = cloud_piercing_partial(view);
    return out;
}

// LookUpFullNormal.
fn cloud_normal(map: texture_2d<f32>, s: sampler, uv: vec2f) -> vec3f {
    let n = textureSample(map, s, uv).xy * 2.0 - 1.0;
    return vec3f(n, sqrt(saturate(1.0 - dot(n, n))));
}

// Its pixel programs (soft, animated, both): the clouds' common pixel program with no lights of its own.
@fragment
fn fs_gta_cloud_hat(in: CloudHatVarying) -> @location(0) vec4f {
    let view = in.eye_ray.xyz;
    let world_z = view.z * in.eye_ray.w + hat.camera.z;

    // The cloud's density and normal (the effect is the same over the draw, so its reads stay in uniform control flow).
    let d1 = textureSample(density_map, hat_sampler, in.uv).g;
    let n1 = cloud_normal(normal_map, hat_sampler, in.uv);
    var density: f32;
    var normal_map_value: vec3f;
    if (hat.effect.x != 0.0) {
        let d2 = textureSample(detail_density_map, hat_detail_sampler, in.uv23.xy).g;
        let d3 = textureSample(detail_density2_map, hat_detail_sampler, in.uv23.zw).g;
        let n2 = cloud_normal(detail_normal_map, hat_detail_sampler, in.uv23.xy);
        let n3 = cloud_normal(detail_normal2_map, hat_detail_sampler, in.uv23.zw);
        let anim = (1.0 - vec3f(d1, d2, d3) * vec3f(d1, d2, d3)) * hat.anim_blend_weights.xyz;
        let combine = anim * hat.anim_combine.xyz;
        let sculpt = anim * hat.anim_sculpt.xyz;
        let combined = in.tangent.a * max(combine.x, max(combine.y, combine.z));
        let sculpted = in.binormal.a + sculpt.x + sculpt.y + sculpt.z;
        density = combined - (1.0 - combined) * sculpted;
        normal_map_value = normalize(n1 * combine.x + n2 * combine.y + n3 * combine.z);
    } else {
        density = 1.0 - d1;
        normal_map_value = n1;
    }
    density = saturate((density - hat.density_shift_scale.x) * hat.density_shift_scale.y);
    var alpha = density * in.normal.a;
    // Cut away under the water's level.
    alpha = select(alpha, 0.0, world_z < hat.water.x);

    let normal = normal_map_value.x * in.tangent.xyz + normal_map_value.y * in.binormal.xyz + normal_map_value.z * in.normal.xyz;

    if (hat.effect.y != 0.0) {
        // As the game's soft particles take it: the depth made the game's (one at the far plane) and back to a view depth: with the
        // viewer's infinite projection Q is one, so -near Q / (z - Q) is near over the depth, kept so for precision.
        let depth = textureLoad(hat_depth, vec2i(in.position.xy), 0);
        let z_depth = 1.0 - depth;
        let z_view = -hat.near_far_q.w / max(depth, 1e-30);
        let range = hat.soft_range.x;
        let diff = select(z_view - in.view_depth, range, z_depth >= 1.0) / range;
        alpha *= saturate(diff);
    }

    // The clouds' light: the hemispheric fill (on the normal's x, z, y) and wrap lighting.
    let hemi = vec3f(normal.x, normal.z, normal.y);
    var fill = hat.west_colour.rgb + saturate(hemi.x * 0.5 + 0.5) * hat.east_minus_west.rgb;
    fill += hat.bounce_colour.rgb * saturate(-hemi.y / 1.25 + 0.25 / 1.25);
    fill += hat.sky_colour.rgb * saturate(hemi.y / 1.75 + 0.75 / 1.75);
    fill *= hat.scale_diffuse_fill_ambient.y;
    let wrap = saturate(dot(normal, hat.sun_direction.xyz) * hat.wrap_lighting.x + hat.wrap_lighting.y);
    let diffuse = fill + hat.sun_colour.rgb * wrap * hat.scale_diffuse_fill_ambient.x;

    let thickness = saturate((1.0 - hat.piercing.w) + (1.0 - density) * hat.piercing.w);
    let scattering = in.scattering * thickness;
    // The light piercing the cloud.
    let pierce_b = saturate(dot(normal, in.piercing.xyz)) * hat.piercing.z;
    let pierce_c = mix(pierce_b * in.piercing.w, in.piercing.w, in.piercing.w);
    let piercing = pierce_c * thickness * hat.sun_colour.rgb * hat.piercing.y;

    var colour = hat.cloud_colour.rgb * (diffuse + hat.ambient_colour.rgb * hat.scale_diffuse_fill_ambient.z);
    colour += scattering + piercing;
    colour = colour * in.fog.a + in.fog.rgb;
    return vec4f(colour, alpha);
}
