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

// The sky's radiance in a unit direction (Z up, +X east).
fn sky_system(sky: SkySystem, d: vec3f) -> vec3f {
    // From east (0) to west (1), through the transition colour.
    let a = sqrt(max(0.5 - 0.5 * d.x, 0.0));
    let p = sky.azimuth_east.w;
    var azimuth: vec3f;
    if (a < p) {
        azimuth = mix(sky.azimuth_east.rgb, sky.azimuth_transition.rgb, a / p);
    } else {
        azimuth = mix(sky.azimuth_transition.rgb, sky.azimuth_west.rgb, (a - p) / (1.0 - p));
    }
    // Up towards the zenith, by height above (or below) the horizon.
    let zc = sky.zenith_constants;
    let blend = mix(zc.y, zc.z, a);
    let toward = azimuth + blend * (sky.zenith_transition.rgb - azimuth);
    let h = abs(d.z);
    var gradient: vec3f;
    if (h < zc.x) {
        gradient = azimuth + (h / zc.x) * blend * (sky.zenith_transition.rgb - azimuth);
    } else {
        let up = saturate((h - zc.x) / (1.0 - zc.x));
        gradient = mix(toward, sky.zenith.rgb, saturate(up / zc.w));
    }
    gradient *= sky.azimuth_west.w;

    // The sun's glow: a Cornette-Shanks phase function, taken against the way away from the sun with g near -1.
    let sc = sky.sun_constants;
    let c = -dot(d, sky.towards_sun.xyz);
    let glow = sc.z * (1.0 + c * c) / pow(abs(sc.y - sc.x * c), 1.5);
    return saturate(1.0 - glow) * gradient + saturate(glow) * sky.sun_colour.rgb * sc.w;
}
