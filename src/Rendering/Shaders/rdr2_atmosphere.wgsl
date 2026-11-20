// Red Dead Redemption 2's atmosphere, as its sky_system effect builds it and its sky_dome effect reads it (checked
// against the game's compiled programs): Bruneton's precomputed scattering, the planet 6360 km across the middle of its
// ground and its air 60 km deep. Shared by the tables' passes (rdr2_sky_tables.wgsl) and the dome (rdr2_sky.wgsl).

const RDR2_GROUND: f32 = 6360000.0;
const RDR2_GROUND_2: f32 = 40449599340544.0;
const RDR2_TOP_2: f32 = 41216401997824.0;
const RDR2_DEPTH: f32 = 60000.0;
const RDR2_INVERSE_4PI: f32 = 0.079577;

// What the game's frame holds of its atmosphere (gShaderLibData_PerFrame from byte 1356, as the sky's update fills it
// from the timecycle and visualsettings.dat), and what the dome reads besides.
struct Rdr2Atmosphere {
    // sky.AltitudeRemapFactor; the Rayleigh and aerosol scale heights in metres; the altitude bias in metres, the height
    // the camera's is taken from.
    heights: vec4f,
    // The Rayleigh scattering coefficients a metre (from the wavelengths and the molecule density), then the aerosols'
    // (Mie) extinction a metre.
    rayleigh_mie: vec4f,
    // The ozone's absorption a metre for each wavelength.
    ozone: vec4f,
    // The sun's intensity times the sky colour (what the Rayleigh scattering is lit by), then what the sky is clamped to:
    // the sun's and the moon's intensities.
    sun_sky: vec4f,
    // The sun's intensity times the sun colour (what the Mie scattering and the disc are lit by), then the disc's
    // intensity multiplier.
    sun_sun: vec4f,
    // The Mie phase function: 3/(8 pi) (1 - g^2)/(2 + g^2), -2g and 1 + g^2, then the Mie intensity.
    phase: vec4f,
    // The unit way towards the sun (Z up), then the tangent of the disc's angular radius.
    sun: vec4f,
    // The curved transmittance's density multiplier and its power, the disc's limb sharpness, and the luminance of the
    // viewer's sky ambient, which the dome's zenith is scaled to.
    disc: vec4f,
};

// The transmittance table's first coordinate for a cosine from the zenith (the inverse of mu = -0.15 + tan(1.5 u) / tan(1.5)
// * 1.15).
fn rdr2_transmittance_u(mu: f32) -> f32 {
    return atan((mu + 0.15) * 12.262105) * 0.666667;
}

// The scattering table's sun coordinate (Bruneton's mu_s, its texels' centres a 64th in from each end).
fn rdr2_sun_u(mu_s: f32) -> f32 {
    return (atan(max(mu_s, -0.1975) * 5.349624) * 0.909091 + 0.74) * 0.484375 + 0.015625;
}

// The scattering table's view coordinate above the horizon: (mu + c) / (1 + c), to the fifth root, where c is the
// cosine down to the horizon; one below it.
fn rdr2_view_u(mu: f32, height: f32) -> f32 {
    let r = height + RDR2_GROUND;
    let c = sqrt(height * (height + 2.0 * RDR2_GROUND)) / r;
    return select(1.0, pow(saturate((mu + c) / (c + 1.0)), 0.2), mu > -c);
}
