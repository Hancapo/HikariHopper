// GTA V's lens flare in its world, as the game draws it (the same in Enhanced), into the HDR frame after
// the world, before the post-process:
//   - How much of the sun shows: the game draws a disc 0.4 times the sun's (sky_system's sun_visibility technique) at the
//     far plane where only the sky is, stippled away where the fog hides it, and counts the pixels that pass with an
//     occlusion query it reads frames later. WebGPU's occlusion queries only say whether any passed, so here the disc's
//     pixels test the world's depth themselves and count themselves; the count is read back a frame or two later, as the
//     game's buffered queries are (ViewportRenderer.LensFlare.cs).
//   - The flare: its elements, laid out on the CPU as the game lays them out (GtaLensFlare.cs), drawn with the im
//     shader's colorcorrectedunlit and colorcorrectedunlitgradient techniques (the artefacts), added to the frame. The
//     rings (the chromatic texture's) are made here from their centre, radii and fade, as the game's strips are.
// Put after scene.wgsl, sky_system.wgsl and gta_sky.wgsl.

struct FlareVisibility {
    // The disc's radius; how much the fog hides of it (the sun's fog factor, which the game sends as noiseSoftness); and the
    // threshold the stipple also discards under (noiseThreshold, which the flare never sets on its shader: nought).
    disc: vec4f,
};

@group(1) @binding(8) var<uniform> flare_visibility: FlareVisibility;
@group(1) @binding(9) var flare_depth: texture_depth_2d;
@group(1) @binding(10) var<storage, read_write> flare_pixels: array<atomic<u32>, 4>;

// The disc's fan: its vertices less its centre and its closing corner.
const SUN_FAN_SIDES: u32 = 22u;

struct SunVisibilityVarying {
    @builtin(position) position: vec4f,
    // How much of the sun the fog leaves.
    @location(0) fog_blend: f32,
};

// vs_sun_visibility: the sun's disc of the visibility's radius, facing the camera, at the far plane; the fog along it.
@vertex
fn vs_gta_sun_visibility(@builtin(vertex_index) index: u32) -> SunVisibilityVarying {
    let triangle = index / 3u;
    let corner = index % 3u;
    var place = vec2f(0.0);
    if (corner != 0u) {
        let angle = f32(triangle + corner - 1u) * 6.2831853 / f32(SUN_FAN_SIDES);
        place = vec2f(cos(angle), sin(angle));
    }
    let world = disc_world(sky.sun_position.xyz, flare_visibility.disc.x, place);
    var out: SunVisibilityVarying;
    out.position = sky_clip(world);
    out.fog_blend = 1.0 - gta_fog_no_haze(sky.fog, normalize(world - sky.camera.xyz) * DISC_FOG_DISTANCE).a * flare_visibility.disc.y;
    return out;
}

// ps_sun_visibility: the fog's stipple (StippleAlpha's 2 by 2 thresholds), then only where the sky is (nothing of the
// world drawn there: depth nought), each pixel that passes counted.
@fragment
fn fs_gta_sun_visibility(in: SunVisibilityVarying) -> @location(0) vec4f {
    let s = fract((in.position.xy - 0.5) * 0.5);
    let alpha_ref = 0.5 * dot(s, vec2f(1.0, 2.0)) + 0.2;
    if (in.fog_blend <= alpha_ref || in.fog_blend < flare_visibility.disc.z) {
        discard;
    }
    if (textureLoad(flare_depth, vec2i(in.position.xy), 0) > 0.0) {
        discard;
    }
    atomicAdd(&flare_pixels[0], 1u);
    return vec4f(0.0);
}

// A sprite, as GtaLensFlareState.Build writes it.
//   A quad: its four corners in clip space, in the game's strip order (two a vector); its texture's cell; its colour
//   (the vertex colour, nought to one) and its emissive scale; the im shader's GeneralParams0 and 1; its texture and
//   kind (nought, or one for the artefacts' gradient).
//   A ring: its centre in clip space and its inner radii; its outer radii and the inner ring's offset; the line from
//   the sun to the centre, and its texture's first U and the step round; its colour and emissive scale; GeneralParams0
//   and 1; its texture, its kind (two), the rings' fade factor and the flare's fade.
struct FlareSprite {
    v: array<vec4f, 7>,
};

@group(1) @binding(11) var<storage, read> flare_sprites: array<FlareSprite>;
@group(1) @binding(12) var flare_sampler: sampler;
@group(1) @binding(13) var flare_texture: texture_2d<f32>;

// The ring's entries round its circle.
const RING_ENTRIES: u32 = 88u;

struct FlareVarying {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    // VS_ColorCorrectedUnlit's colour: the vertex colour squared, times the emissive scale.
    @location(1) colour: vec3f,
    @location(2) @interpolate(flat) params0: vec4f,
    @location(3) @interpolate(flat) params1: vec4f,
    @location(4) @interpolate(flat) kind: f32,
};

fn flare_varying(s: FlareSprite, place: vec2f, uv: vec2f, colour: vec3f) -> FlareVarying {
    var out: FlareVarying;
    out.position = vec4f(place, 0.0, 1.0);
    out.uv = uv;
    out.colour = colour * colour * s.v[3].w;
    out.params0 = s.v[4];
    out.params1 = s.v[5];
    out.kind = s.v[6].y;
    return out;
}

// A quad's six corners from its strip of four.
@vertex
fn vs_gta_flare_quad(@builtin(vertex_index) index: u32, @builtin(instance_index) instance: u32) -> FlareVarying {
    let s = flare_sprites[instance];
    var strip = array<u32, 6>(0u, 1u, 2u, 2u, 1u, 3u);
    let corner = strip[index];
    let pair = s.v[corner / 2u];
    let place = select(pair.xy, pair.zw, corner % 2u == 1u);
    let cell = s.v[2];
    let uv = vec2f(select(cell.z, cell.x, corner % 2u == 1u), select(cell.y, cell.w, corner >= 2u));
    return flare_varying(s, place, uv, s.v[3].rgb);
}

// A ring's strip, as a list: each entry round it an outer corner (V one) and an inner one (V nought, offset towards the
// sun), the texture's U stepping round; its colour faded on the side away from the screen's centre, and by the flare's
// fade.
@vertex
fn vs_gta_flare_ring(@builtin(vertex_index) index: u32, @builtin(instance_index) instance: u32) -> FlareVarying {
    let s = flare_sprites[instance];
    let strip = index / 3u + index % 3u;
    let entry = strip / 2u;
    let inner = strip % 2u == 1u;
    let angle = f32(entry % RING_ENTRIES) * 6.2831853 / f32(RING_ENTRIES);
    let sin_cos = vec2f(sin(angle), cos(angle));
    var place = s.v[0].xy + s.v[1].xy * sin_cos;
    if (inner) {
        place = s.v[0].xy + s.v[0].zw * sin_cos + s.v[1].zw;
    }
    let u = s.v[2].z + f32(entry) * s.v[2].w;
    let fade = s.v[6].w;
    let towards = clamp((0.5 * dot(s.v[2].xy * vec2f(1.0, -1.0), sin_cos) + 0.5) * s.v[6].z, 0.0, 1.0);
    let colour = s.v[3].rgb * (1.0 - towards * fade) * (1.0 - fade);
    return flare_varying(s, place, vec2f(u, select(1.0, 0.0, inner)), colour);
}

// PS_ColorCorrectedUnlit (desaturated by GeneralParams1's z, towards a grey boosted by its w) and, for the artefacts,
// PS_ColorCorrectedUnlitGradient (darkened towards the side facing the sun): the texture squared, times the colour.
@fragment
fn fs_gta_flare(in: FlareVarying) -> @location(0) vec4f {
    var colour = textureSample(flare_texture, flare_sampler, in.uv);
    if (abs(in.kind - 1.0) < 0.5) {
        let pixel = in.position.xy / scene.viewport.zw;
        let along = saturate(dot((pixel - in.params0.zw) / in.params1.xy, in.params0.xy));
        return vec4f(colour.rgb * colour.rgb * in.colour * along * along, colour.a);
    }
    let grey = (colour.r + colour.g + colour.b) / 3.0 * in.params1.w;
    let shade = mix(colour.rgb, vec3f(grey), in.params1.z);
    return vec4f(shade * shade * in.colour, colour.a);
}
