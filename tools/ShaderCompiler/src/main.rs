//! Compiles every WGSL shader module HikariHopper makes to DXIL, for its Direct3D 12 builds.
//!
//! The modules are those listed in `Shaders/modules.txt`: each a name and the `.wgsl` files it joins, with a newline
//! between each, exactly as `ViewportRenderer.Module` joins them at run time. Each module's entry points are translated
//! to HLSL by Naga (the version wgpu draws with) and compiled to DXIL by DXC, into one pack the app embeds. The app finds
//! a module's DXIL by a hash of its WGSL, so a module that isn't listed, or changed without being rebuilt, is never
//! drawn with stale code.
//!
//! Registers follow the module alone, not a pipeline layout, so one compile serves every pipeline that uses it:
//!
//! - a binding `@group(g) @binding(b)` is register `b` in space `g + 1`, of the kind its type takes (`b`, `t`, `u`);
//! - samplers are indices into one sampler heap (`s0` to `s2047` and, for comparison samplers, `s2048` up, both in
//!   space 0), each group's indices read from a buffer at `t1000` in its space, its sampler at binding `b` at index `b`;
//! - Naga's special constants (first vertex and instance, workgroup counts) are root constants at `b0`, space 0.
//!
//! A vertex entry point is compiled once on its own and once with each fragment entry point of its module, as Naga
//! leaves out what the vertex stage gives that the fragment stage doesn't read.
//!
//! Usage: `shader-compiler <modules.txt> <resources.txt> <dxc.exe> <cache folder> <pack>`, the resources file naming
//! each shader file the way the app's resources do: `Gta5/sky.wgsl=C:\...\sky.wgsl` a line.

use std::collections::{BTreeSet, HashMap};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::Mutex;
use std::sync::atomic::{AtomicUsize, Ordering};

use naga::back::hlsl;
use naga::valid::{Capabilities, ValidationFlags, Validator};

const SAMPLER_INDEX_REGISTER: u32 = 1000;
const PACK_VERSION: u32 = 1;

struct Module {
    name: String,
    code: String,
    hash: u64,
}

struct Job {
    module: usize,
    stage: naga::ShaderStage,
    entry: String,
    fragment: Option<String>,
}

struct Compiled {
    module: usize,
    stage: naga::ShaderStage,
    entry: String,
    fragment: Option<String>,
    dxil: Vec<u8>,
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() != 6 {
        eprintln!("usage: shader-compiler <modules.txt> <resources.txt> <dxc.exe> <cache folder> <pack>");
        std::process::exit(2);
    }
    if let Err(error) = run(&args[1], &args[2], Path::new(&args[3]), Path::new(&args[4]), Path::new(&args[5])) {
        eprintln!("shader-compiler: error: {error}");
        std::process::exit(1);
    }
}

fn run(manifest: &str, resources: &str, dxc: &Path, cache: &Path, pack: &Path) -> Result<(), String> {
    let files: HashMap<String, PathBuf> = read_text(Path::new(resources))?
        .lines()
        .filter_map(|line| line.split_once('='))
        .map(|(name, path)| (name.trim().to_string(), PathBuf::from(path.trim())))
        .collect();
    let mut modules = Vec::new();
    for line in read_text(Path::new(manifest))?.lines() {
        let line = line.trim();
        if line.is_empty() || line.starts_with('#') {
            continue;
        }
        let (name, list) = line.split_once('=').ok_or_else(|| format!("modules.txt: no '=' in {line:?}"))?;
        let mut parts = Vec::new();
        for file in list.split_whitespace() {
            let path = files.get(file).ok_or_else(|| format!("module {}: no shader file is named {file}", name.trim()))?;
            parts.push(read_text(path)?);
        }
        let code = parts.join("\n");
        let hash = fnv1a(code.as_bytes());
        modules.push(Module { name: name.trim().to_string(), code, hash });
    }
    std::fs::create_dir_all(cache).map_err(|error| format!("{}: {error}", cache.display()))?;

    // What each module has to be compiled as: every entry point, vertex ones with each fragment one too.
    let mut parsed = Vec::new();
    let mut jobs = Vec::new();
    for (index, module) in modules.iter().enumerate() {
        let ir = naga::front::wgsl::parse_str(&module.code).map_err(|error| format!("module {}: {}", module.name, error.emit_to_string(&module.code)))?;
        let info = Validator::new(ValidationFlags::all(), Capabilities::all())
            .validate(&ir)
            .map_err(|error| format!("module {}: {}", module.name, error.emit_to_string(&module.code)))?;
        let fragments: Vec<String> =
            ir.entry_points.iter().filter(|entry| entry.stage == naga::ShaderStage::Fragment).map(|entry| entry.name.clone()).collect();
        for entry in &ir.entry_points {
            jobs.push(Job { module: index, stage: entry.stage, entry: entry.name.clone(), fragment: None });
            if entry.stage == naga::ShaderStage::Vertex {
                for fragment in &fragments {
                    jobs.push(Job { module: index, stage: entry.stage, entry: entry.name.clone(), fragment: Some(fragment.clone()) });
                }
            }
        }
        parsed.push((ir, info));
    }

    // Translated, then each distinct HLSL compiled once, on every core; what was compiled before comes from the cache.
    let mut translated = Vec::with_capacity(jobs.len());
    let mut errors = Vec::new();
    for job in &jobs {
        let (ir, info) = &parsed[job.module];
        match translate(ir, info, job) {
            Ok(hlsl) => translated.push(hlsl),
            Err(error) => errors.push(describe(&modules, job, &error)),
        }
    }
    if !errors.is_empty() {
        return Err(errors.join("\n"));
    }
    let mut unique: Vec<&Hlsl> = Vec::new();
    let mut seen = BTreeSet::new();
    for hlsl in &translated {
        if seen.insert(hlsl.key) {
            unique.push(hlsl);
        }
    }
    let next = AtomicUsize::new(0);
    let dxil = Mutex::new(HashMap::new());
    let failures = Mutex::new(Vec::new());
    let workers = std::thread::available_parallelism().map_or(4, |count| count.get());
    std::thread::scope(|scope| {
        for _ in 0..workers {
            scope.spawn(|| {
                loop {
                    let index = next.fetch_add(1, Ordering::Relaxed);
                    let Some(hlsl) = unique.get(index) else { break };
                    match compile(hlsl, dxc, cache) {
                        Ok(bytes) => {
                            dxil.lock().unwrap().insert(hlsl.key, bytes);
                        }
                        Err(error) => failures.lock().unwrap().push(error),
                    }
                }
            });
        }
    });
    let failures = failures.into_inner().unwrap();
    if !failures.is_empty() {
        return Err(failures.join("\n"));
    }
    let dxil = dxil.into_inner().unwrap();

    // A vertex entry point with a fragment one goes in only where it differs from the vertex entry point alone.
    let alone: HashMap<(usize, &str), u64> = jobs
        .iter()
        .zip(&translated)
        .filter(|(job, _)| job.fragment.is_none())
        .map(|(job, hlsl)| ((job.module, job.entry.as_str()), hlsl.key))
        .collect();
    let mut compiled = Vec::new();
    for (job, hlsl) in jobs.iter().zip(&translated) {
        if job.fragment.is_some() && alone.get(&(job.module, job.entry.as_str())) == Some(&hlsl.key) {
            continue;
        }
        compiled.push(Compiled {
            module: job.module,
            stage: job.stage,
            entry: job.entry.clone(),
            fragment: job.fragment.clone(),
            dxil: dxil[&hlsl.key].clone(),
        });
    }
    write_pack(pack, &modules, &compiled)?;
    let bytes: usize = compiled.iter().map(|entry| entry.dxil.len()).sum();
    println!(
        "shader-compiler: {} modules, {} entry points ({} compiled), {} KB of DXIL",
        modules.len(),
        compiled.len(),
        unique.len(),
        bytes / 1024
    );
    Ok(())
}

fn describe(modules: &[Module], job: &Job, error: &str) -> String {
    let with = job.fragment.as_ref().map_or(String::new(), |fragment| format!(" (with {fragment})"));
    format!("module {}, {}{with}: {error}", modules[job.module].name, job.entry)
}

/// An entry point's HLSL, and what DXC compiles it as.
struct Hlsl {
    key: u64,
    source: String,
    entry: String,
    target: String,
}

fn translate(ir: &naga::Module, info: &naga::valid::ModuleInfo, job: &Job) -> Result<Hlsl, String> {
    let (ir, info) = naga::back::pipeline_constants::process_overrides(ir, info, None, &Default::default())
        .map_err(|error| format!("{error:?}"))?;
    let options = options(&ir);
    let pipeline = hlsl::PipelineOptions { entry_point: Some((job.stage, job.entry.clone())) };
    let fragment = match &job.fragment {
        Some(name) => Some(hlsl::FragmentEntryPoint::new(&ir, name).ok_or_else(|| format!("no fragment entry point {name}"))?),
        None => None,
    };
    let mut source = String::new();
    let reflection = hlsl::Writer::new(&mut source, &options, &pipeline)
        .write(&ir, &info, fragment.as_ref())
        .map_err(|error| format!("HLSL: {error}"))?;
    let entry = reflection
        .entry_point_names
        .into_iter()
        .next()
        .ok_or("no entry point written")?
        .map_err(|error| format!("HLSL: {error:?}"))?;
    let target = format!(
        "{}_{}",
        match job.stage {
            naga::ShaderStage::Vertex => "vs",
            naga::ShaderStage::Fragment => "ps",
            naga::ShaderStage::Compute => "cs",
            other => return Err(format!("{other:?} shaders aren't drawn with")),
        },
        options.shader_model.to_str()
    );
    let key = fnv1a(format!("{target}\0{entry}\0{source}").as_bytes());
    Ok(Hlsl { key, source, entry, target })
}

fn compile(hlsl: &Hlsl, dxc: &Path, cache: &Path) -> Result<Vec<u8>, String> {
    let dxil_path = cache.join(format!("{:016x}.dxil", hlsl.key));
    if let Ok(dxil) = std::fs::read(&dxil_path) {
        return Ok(dxil);
    }
    let hlsl_path = cache.join(format!("{:016x}.hlsl", hlsl.key));
    std::fs::write(&hlsl_path, &hlsl.source).map_err(|error| format!("{}: {error}", hlsl_path.display()))?;
    let partial = cache.join(format!("{:016x}.dxil.part", hlsl.key));
    // As wgpu compiles Naga's HLSL: HLSL 2018, strict.
    let output = Command::new(dxc)
        .args(["-nologo", "-E", &hlsl.entry, "-T", &hlsl.target, "-HV", "2018", "-Ges", "-no-warnings", "-Fo"])
        .arg(&partial)
        .arg(&hlsl_path)
        .output()
        .map_err(|error| format!("{}: {error}", dxc.display()))?;
    if !output.status.success() {
        return Err(format!("DXC failed on {}:\n{}", hlsl_path.display(), String::from_utf8_lossy(&output.stderr)));
    }
    std::fs::rename(&partial, &dxil_path).map_err(|error| format!("{}: {error}", dxil_path.display()))?;
    std::fs::read(&dxil_path).map_err(|error| format!("{}: {error}", dxil_path.display()))
}

/// The registers every binding of the module takes, from the module alone.
fn options(ir: &naga::Module) -> hlsl::Options {
    let mut binding_map = hlsl::BindingMap::default();
    let mut sampled_groups = BTreeSet::new();
    for (_, global) in ir.global_variables.iter() {
        let Some(binding) = global.binding else { continue };
        let (inner, array) = match ir.types[global.ty].inner {
            naga::TypeInner::BindingArray { base, size } => (
                &ir.types[base].inner,
                match size {
                    naga::ArraySize::Constant(count) => Some(count.get()),
                    _ => None,
                },
            ),
            ref inner => (inner, None),
        };
        let target = if matches!(inner, naga::TypeInner::Sampler { .. }) {
            sampled_groups.insert(binding.group);
            hlsl::BindTarget { space: 255, register: binding.binding, binding_array_size: array, ..Default::default() }
        } else {
            hlsl::BindTarget {
                space: (binding.group + 1) as u8,
                register: binding.binding,
                binding_array_size: array,
                // Uniform buffers may be root descriptors, which Direct3D 12 doesn't bounds-check.
                restrict_indexing: global.space == naga::AddressSpace::Uniform,
                ..Default::default()
            }
        };
        binding_map.insert(binding, target);
    }
    let sampler_buffer_binding_map = sampled_groups
        .into_iter()
        .map(|group| {
            (
                hlsl::SamplerIndexBufferKey { group },
                hlsl::BindTarget { space: (group + 1) as u8, register: SAMPLER_INDEX_REGISTER, ..Default::default() },
            )
        })
        .collect();
    hlsl::Options {
        shader_model: hlsl::ShaderModel::V6_0,
        binding_map,
        fake_missing_bindings: false,
        special_constants_binding: Some(hlsl::BindTarget { space: 0, register: 0, ..Default::default() }),
        sampler_heap_target: hlsl::SamplerHeapBindTargets {
            standard_samplers: hlsl::BindTarget { space: 0, register: 0, ..Default::default() },
            comparison_samplers: hlsl::BindTarget { space: 0, register: 2048, ..Default::default() },
        },
        sampler_buffer_binding_map,
        ..Default::default()
    }
}

/// The pack: `HHDX`, its version and module count, then each module's WGSL hash, name and compiled entry points (their
/// stage, name, the fragment entry point a vertex one was compiled with, if any, and DXIL). Little-endian throughout.
fn write_pack(path: &Path, modules: &[Module], compiled: &[Compiled]) -> Result<(), String> {
    let mut out = Vec::new();
    out.extend_from_slice(b"HHDX");
    out.extend_from_slice(&PACK_VERSION.to_le_bytes());
    out.extend_from_slice(&(modules.len() as u32).to_le_bytes());
    for (index, module) in modules.iter().enumerate() {
        out.extend_from_slice(&module.hash.to_le_bytes());
        text(&mut out, &module.name);
        let entries: Vec<&Compiled> = compiled.iter().filter(|entry| entry.module == index).collect();
        out.extend_from_slice(&(entries.len() as u32).to_le_bytes());
        for entry in entries {
            out.push(match entry.stage {
                naga::ShaderStage::Vertex => 0,
                naga::ShaderStage::Fragment => 1,
                _ => 2,
            });
            text(&mut out, &entry.entry);
            text(&mut out, entry.fragment.as_deref().unwrap_or(""));
            out.extend_from_slice(&(entry.dxil.len() as u32).to_le_bytes());
            out.extend_from_slice(&entry.dxil);
        }
    }
    if let Some(folder) = path.parent() {
        std::fs::create_dir_all(folder).map_err(|error| format!("{}: {error}", folder.display()))?;
    }
    std::fs::write(path, out).map_err(|error| format!("{}: {error}", path.display()))
}

fn text(out: &mut Vec<u8>, value: &str) {
    out.extend_from_slice(&(value.len() as u16).to_le_bytes());
    out.extend_from_slice(value.as_bytes());
}

/// A file as the app reads its resources: UTF-8, without a byte order mark.
fn read_text(path: &Path) -> Result<String, String> {
    let bytes = std::fs::read(path).map_err(|error| format!("{}: {error}", path.display()))?;
    let bytes = bytes.strip_prefix(b"\xEF\xBB\xBF").unwrap_or(&bytes);
    String::from_utf8(bytes.to_vec()).map_err(|error| format!("{}: {error}", path.display()))
}

/// FNV-1a, 64 bits: the hash the app looks modules up by.
fn fnv1a(bytes: &[u8]) -> u64 {
    let mut hash = 0xcbf2_9ce4_8422_2325u64;
    for &byte in bytes {
        hash ^= u64::from(byte);
        hash = hash.wrapping_mul(0x0100_0000_01b3);
    }
    hash
}
