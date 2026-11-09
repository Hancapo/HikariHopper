// GTA V Enhanced's bloom (postfx's PS_CalcBloom0, then the bloom effect's downsample and upsample filters), from the
// drawables' HDR target to a chain of eight levels, half the window down to 1/256 of it:
//   bright pass to a target half the window (Bloom Buffer Unfiltered) -> filtered into the first level, the same
//   size, weighted against fireflies (Karis's average) -> downsampled down the chain -> tent upsampled back up it,
//   each blended halfway into the level above
// The tone map adds the first level, the bloom, to the drawables before its filmic curve. The source is the HDR
// target for the bright pass, then what the pass before drew: one level each, so its size is the level's.
// Scene comes from scene.wgsl, the light from light.wgsl.

@group(1) @binding(0) var bloom_source: texture_2d<f32>;
@group(1) @binding(1) var bloom_sampler: sampler;
@group(1) @binding(2) var<storage, read> frame_mean: array<f32, 4>;

struct BloomVarying {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
};

// One triangle over the target, with its texture coordinates.
@vertex
fn vs_bloom(@builtin(vertex_index) index: u32) -> BloomVarying {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    var out: BloomVarying;
    out.position = vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
    out.uv = vec2f(corner.x, 1.0 - corner.y);
    return out;
}

fn source_texel() -> vec2f {
    return 1.0 / vec2f(textureDimensions(bloom_source));
}

fn tap(uv: vec2f) -> vec3f {
    return textureSampleLevel(bloom_source, bloom_sampler, uv, 0.0).rgb;
}

// PS_CalcBloom0: a 3x3 box of the HDR target, exposed and over the filmic curve's white point (Filmic0.w), kept
// where its mean is past the threshold, faded in across the width (BloomParams). What isn't a number is dropped.
@fragment
fn fs_bloom_bright(in: BloomVarying) -> @location(0) vec4f {
    let texel = source_texel();
    var sum = vec3f(0.0);
    for (var y = -1; y <= 1; y++) {
        for (var x = -1; x <= 1; x++) {
            sum += tap(in.uv + vec2f(f32(x), f32(y)) * texel);
        }
    }
    let stops = exposure_stops(frame_mean[0]);
    let t = filmic_blend(stops);
    let white = mix(light.filmic_bright1.z, light.filmic_dark1.z, t);
    // The HDR target is premultiplied by coverage: what isn't covered adds nothing.
    let colour = min(sum / 9.0, vec3f(30000.0)) * exp2(stops) / white;
    let luminance = max((colour.r + colour.g + colour.b) / 3.0, 1.4e-4);
    let bright = colour * saturate((luminance - light.bloom.x) * light.bloom.z);
    let finite = (bright == bright) & (abs(bright) < vec3f(3.0e38));
    return vec4f(select(vec3f(0.0), bright, finite), 0.0);
}

fn luma(colour: vec3f) -> f32 {
    return dot(colour, vec3f(0.2126, 0.7152, 0.0722));
}

// The bloom filter's 13 taps around the target's pixel, in the source's texels: the middle, four one texel off
// diagonally, and eight two texels off. Grouped into the inner four (half the result) and four squares of four, one
// per corner (an eighth each), each square sharing the middle.
struct Taps {
    centre: vec3f,
    inner: array<vec3f, 4>,
    outer: array<vec3f, 8>,
};

fn taps(uv: vec2f) -> Taps {
    let texel = source_texel();
    var t: Taps;
    t.centre = tap(uv);
    t.inner[0] = tap(uv + vec2f(-1.0, -1.0) * texel);
    t.inner[1] = tap(uv + vec2f(1.0, -1.0) * texel);
    t.inner[2] = tap(uv + vec2f(-1.0, 1.0) * texel);
    t.inner[3] = tap(uv + vec2f(1.0, 1.0) * texel);
    // Row by row: top left, top, top right, left, right, bottom left, bottom, bottom right.
    t.outer[0] = tap(uv + vec2f(-2.0, -2.0) * texel);
    t.outer[1] = tap(uv + vec2f(0.0, -2.0) * texel);
    t.outer[2] = tap(uv + vec2f(2.0, -2.0) * texel);
    t.outer[3] = tap(uv + vec2f(-2.0, 0.0) * texel);
    t.outer[4] = tap(uv + vec2f(2.0, 0.0) * texel);
    t.outer[5] = tap(uv + vec2f(-2.0, 2.0) * texel);
    t.outer[6] = tap(uv + vec2f(0.0, 2.0) * texel);
    t.outer[7] = tap(uv + vec2f(2.0, 2.0) * texel);
    return t;
}

// Four taps' mean, each weighted by 1 / (1 + its luma) when karis is set, so one very bright texel can't flicker.
fn group(a: vec3f, b: vec3f, c: vec3f, d: vec3f, karis: bool) -> vec3f {
    if (!karis) {
        return (a + b + c + d) * 0.25;
    }
    let wa = 1.0 / (1.0 + luma(a));
    let wb = 1.0 / (1.0 + luma(b));
    let wc = 1.0 / (1.0 + luma(c));
    let wd = 1.0 / (1.0 + luma(d));
    return (a * wa + b * wb + c * wc + d * wd) / (wa + wb + wc + wd);
}

fn downsample(uv: vec2f, karis: bool) -> vec3f {
    let t = taps(uv);
    let o = t.outer;
    let inner = group(t.inner[0], t.inner[1], t.inner[2], t.inner[3], karis);
    let corners = group(o[0], o[1], o[3], t.centre, karis) + group(o[1], o[2], t.centre, o[4], karis)
        + group(o[3], t.centre, o[5], o[6], karis) + group(t.centre, o[4], o[6], o[7], karis);
    // DownsampleColor is white.
    return inner * 0.5 + corners * 0.125;
}

// PS_DownSampleBloomFilterKaris: the bright pass into the first level.
@fragment
fn fs_bloom_downsample_karis(in: BloomVarying) -> @location(0) vec4f {
    return vec4f(downsample(in.uv, true), 0.0);
}

// PS_DownSampleBloomFilter: the steps after it.
@fragment
fn fs_bloom_downsample(in: BloomVarying) -> @location(0) vec4f {
    return vec4f(downsample(in.uv, false), 0.0);
}

// PS_UpSampleBloomFilterBlend: a 3x3 tent of the level below, a source texel apart, blended over this level by
// UpsampleWeight (a half).
@fragment
fn fs_bloom_upsample(in: BloomVarying) -> @location(0) vec4f {
    let texel = source_texel();
    var sum = tap(in.uv) * 0.25;
    sum += (tap(in.uv + vec2f(0.0, -1.0) * texel) + tap(in.uv + vec2f(-1.0, 0.0) * texel)
        + tap(in.uv + vec2f(1.0, 0.0) * texel) + tap(in.uv + vec2f(0.0, 1.0) * texel)) * 0.125;
    sum += (tap(in.uv + vec2f(-1.0, -1.0) * texel) + tap(in.uv + vec2f(1.0, -1.0) * texel)
        + tap(in.uv + vec2f(-1.0, 1.0) * texel) + tap(in.uv + vec2f(1.0, 1.0) * texel)) * 0.0625;
    return vec4f(sum, 0.5);
}
