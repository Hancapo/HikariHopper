using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// The world's GPU side, without a game: every game's pipelines and a frame of it, with its shadow, raise no GPU errors.
public sealed class WorldRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_worlds_pipelines_compile_and_its_frames_raise_no_GPU_errors(bool direct)
    {
        ViewportRenderer.DirectWorldDraws = direct;
        GpuDevice gpu = GpuDevice.Shared;
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            renderer.CreateWorldPipelines();
            renderer.CreatePickPipelines();
            // Each game's: RDR2's material, and GTA V's surfaces and terrain, drawn, cast and picked.
            foreach (WorldFamily family in Enum.GetValues<WorldFamily>())
            {
                renderer.CreateWorldFamily(family);
                renderer.CreatePickFamily(family);
            }
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Shadows = true;
            renderer.Camera.Frame(new Vector3(-300, 740, 132), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(-300, 740, 132), -MathF.PI / 2, .18f);
            renderer.SetTime(14);
            renderer.RenderTo(target.CreateView());
            // Nothing streamed: nothing under the pointer.
            Assert.Null(renderer.PickWorldAt(32, 16));
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            Assert.Equal(WorldShadowCount, renderer.CascadesCast);
            renderer.Dispose();
            target.Destroy();
        }
        ViewportRenderer.DirectWorldDraws = false;
    }

    [Fact]
    public void A_texture_read_again_for_another_asset_is_the_one_on_the_GPU_and_its_pixels_go()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            var weak = new List<WeakReference>();
            // Two assets reading the same dictionary's texture, each its own read of it: different objects, one texture.
            void Add(uint hash)
            {
                var dictionary = new FiveFury.Textures.TextureDictionary();
                var texture = new FiveFury.Textures.Texture("road_d", 4, 4, CommonFury.Textures.TextureFormat.BC1Unorm, 1, new byte[8]);
                dictionary.Textures.Add(texture);
                weak.Add(new WeakReference(texture));
                FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle(diffuseName: "road_d")).Drawable;
                DrawableScene scene = DrawableScene.Build(drawable, [dictionary]);
                Assert.Same(texture, scene.Materials.Single().Diffuse);
                var asset = Rendering.World.WorldAsset.FromGta(new RedFury.World.WorldAssetKey(hash, 2), scene, [drawable],
                    read => new Rendering.World.WorldTextureId(7, 9));
                Assert.True(renderer.AddWorldAsset(asset, new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
                while (renderer.HasPendingWorldUpload)
                    renderer.ProcessWorldUploads();
            }
            Add(1);
            Add(2);
            Assert.True(renderer.IsWorldReady(new RedFury.World.WorldAssetKey(1, 2)) && renderer.IsWorldReady(new RedFury.World.WorldAssetKey(2, 2)));
            Assert.Equal(1, renderer.WorldTextureCount);
            // Up, nothing holds the textures read (nor their pixels).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.All(weak, reference => Assert.False(reference.IsAlive));
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.ClearWorld();
            Assert.Equal(0, renderer.WorldTextureCount);
            renderer.Dispose();
        }
    }

    [Fact]
    public void A_moving_camera_s_frame_is_drawn_from_where_the_camera_is_now()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Camera.Frame(new Vector3(195, -1030, 62), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(195, -1030, 62), -MathF.PI / 2, .2f);
            renderer.RenderTo(view);
            // Flown 30 m sideways between frames: the point 10 m ahead of the camera now is in the middle of this frame, its
            // origin the camera, not where it was the frame before.
            renderer.Camera.Fly(0, 1, 0, 1);
            renderer.RenderTo(view);
            var (matrix, origin) = renderer.SceneView;
            Assert.Equal(renderer.Camera.Eye, origin);
            Vector4 ahead = Vector4.Transform(new Vector4(renderer.Camera.Eye + renderer.Camera.Forward * 10 - origin, 1), matrix);
            Assert.Equal(0, ahead.X / ahead.W, 3);
            Assert.Equal(0, ahead.Y / ahead.W, 3);
            renderer.Dispose();
            target.Destroy();
        }
    }

    [Fact]
    public void A_placement_leaving_the_coverage_fades_out_while_the_one_replacing_it_fades_in()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Camera.Frame(new Vector3(0, -5, 1), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(0, -5, 1), -MathF.PI / 2, 0);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            // A low-detail model and the one replacing it, both up.
            RedFury.World.WorldLodNode Place(uint hash, int index)
            {
                FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle()).Drawable;
                var key = new RedFury.World.WorldAssetKey(hash, 2);
                Assert.True(renderer.AddWorldAsset(Rendering.World.WorldAsset.FromGta(key, DrawableScene.Build(drawable, []), [drawable]),
                    new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
                while (renderer.HasPendingWorldUpload)
                    renderer.ProcessWorldUploads();
                return new RedFury.World.WorldLodNode(new RedFury.World.WorldEntityId(1, index), null, key, Matrix4x4.Identity, new Vector3(.5f, .5f, 0), 1, 100, 0, 0);
            }
            RedFury.World.WorldLodNode low = Place(1, 0), high = Place(2, 1);
            renderer.SetWorldSelection(new RedFury.World.WorldSelection([low], [low.Asset]));
            renderer.RenderTo(view);
            Assert.Equal(0, renderer.WorldFadingOut);
            // Replaced: it stays drawn, fading out, as long as the fade lasts, then goes.
            renderer.SetWorldSelection(new RedFury.World.WorldSelection([high], [high.Asset]));
            renderer.RenderTo(view);
            Assert.Equal(1, renderer.WorldFadingOut);
            Thread.Sleep(TimeSpan.FromSeconds(ViewportRenderer.LodFadeSeconds + .15));
            renderer.RenderTo(view);
            Assert.Equal(0, renderer.WorldFadingOut);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.Dispose();
            target.Destroy();
        }
    }

    private const long WorldShadowCount = Rendering.World.WorldShadowCascades.Count;
}
