// GTA V's sky, as its sky_system effect draws it (vs/ps_sky_normal_sun) from the constants the game derives from the
// timecycle (SkySystem.cs): the gradient from east to west and from the horizon to the zenith, then the sun's glow
// over it. The game works the gradient out per vertex of its sky dome; here it is per direction. Its fog, dither and
// exposure are left to whoever draws it, as are the clouds, stars, moon and the sun's disc, which are their own
// passes in the game.

struct SkySystem {
    // azimuthEastColor, with azimuthTransitionPosition in w.
    azimuth_east: vec4f,
    // azimuthWestColor, with hdrIntensity in w.
    azimuth_west: vec4f,
    // azimuthTransitionColor.
    azimuth_transition: vec4f,
    // zenithColor.
    zenith: vec4f,
    // zenithTransitionColor.
    zenith_transition: vec4f,
    // zenithConstants: the transition's height, its east and west blends, one less the zenith blend's start.
    zenith_constants: vec4f,
    // sunColorHdr.
    sun_colour: vec4f,
    // sunConstants: 2g, 1 + g², the glow's scale, its intensity.
    sun_constants: vec4f,
    // Unit direction towards the sun.
    towards_sun: vec4f,
    // What the viewer puts below the horizon, where the game has its world: not part of the sky.
    ground: vec4f,
};

// The gradient: from east to west through the transition colour by the azimuth blend a (0 east,
// 1 west), and up towards the zenith by the height h above (or below) the horizon, a direction's |z|.
fn sky_gradient(east: vec3f, transition: vec3f, west: vec3f, position: f32, zenith: vec3f, zenith_transition: vec3f,
    zc: vec4f, a: f32, h: f32) -> vec3f {
    var azimuth: vec3f;
    if (a < position) {
        azimuth = mix(east, transition, a / position);
    } else {
        azimuth = mix(transition, west, (a - position) / (1.0 - position));
    }
    let blend = mix(zc.y, zc.z, a);
    let toward = azimuth + blend * (zenith_transition - azimuth);
    if (h < zc.x) {
        return azimuth + (h / zc.x) * blend * (zenith_transition - azimuth);
    }
    let up = saturate((h - zc.x) / (1.0 - zc.x));
    return mix(toward, zenith, saturate(up / zc.w));
}

// The sky's radiance in a unit direction (Z up, +X east).
fn sky_system(sky: SkySystem, d: vec3f) -> vec3f {
    let a = sqrt(max(0.5 - 0.5 * d.x, 0.0));
    let gradient = sky_gradient(sky.azimuth_east.rgb, sky.azimuth_transition.rgb, sky.azimuth_west.rgb, sky.azimuth_east.w,
        sky.zenith.rgb, sky.zenith_transition.rgb, sky.zenith_constants, a, abs(d.z)) * sky.azimuth_west.w;

    // The sun's glow: a Cornette-Shanks phase function, taken against the way away from the sun with g near -1.
    let sc = sky.sun_constants;
    let c = -dot(d, sky.towards_sun.xyz);
    let glow = sc.z * (1.0 + c * c) / pow(abs(sc.y - sc.x * c), 1.5);
    return saturate(1.0 - glow) * gradient + saturate(glow) * sky.sun_colour.rgb * sc.w;
}
