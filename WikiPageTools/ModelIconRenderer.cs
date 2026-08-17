using System.Diagnostics;
using System.Reflection;
using EntityPageTools;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.CompiledShader;

namespace FGDDumper
{
    /// <summary>
    /// Renders a png of an entity's model, for the entities whose FGD names one. Source2Viewer's
    /// own renderer does the drawing, into an offscreen window, so the icons look like its
    /// thumbnails.
    ///
    /// This needs a GPU and an OpenGL context, which is fine because dumping already needs the
    /// games installed. Nothing on the wiki side knows this happened, the entity just ends up with
    /// an IconPath like any other.
    /// </summary>
    public sealed class ModelIconRenderer : IDisposable
    {
        private const int IconSize = 256;

        // rendered larger than the icon, so cropping to the model and scaling back down stays sharp
        private const int RenderSize = 512;

        // anything at or below this is background bleed, not the model
        private const byte AlphaFloor = 8;

        private static readonly float CameraYaw = float.DegreesToRadians(225f);
        private static readonly float CameraPitch = float.DegreesToRadians(20f);

        private readonly NativeWindow window;
        private readonly RendererContext rendererContext;
        private readonly Renderer renderer;
        private readonly Framebuffer framebuffer;
        private readonly TextRenderer textRenderer;
        private Framebuffer? saveBuffer;

        private ModelIconRenderer(NativeWindow window, RendererContext rendererContext, Renderer renderer, Framebuffer framebuffer, TextRenderer textRenderer)
        {
            this.window = window;
            this.rendererContext = rendererContext;
            this.renderer = renderer;
            this.framebuffer = framebuffer;
            this.textRenderer = textRenderer;
        }

        /// <summary>
        /// Brings up an offscreen renderer reading from a game's files, or null when no OpenGL
        /// context can be had. A machine without a usable GPU should still be able to dump
        /// everything else, so this never throws.
        /// </summary>
        public static ModelIconRenderer? TryCreate(GameFileLoader fileLoader)
        {
            try
            {
                var settings = new NativeWindowSettings
                {
                    APIVersion = GLEnvironment.RequiredVersion,
                    Vsync = VSyncMode.Off,
                    WindowBorder = WindowBorder.Hidden,
                    WindowState = WindowState.Normal,
                    Title = "Entity icon renderer",
                    Flags = ContextFlags.ForwardCompatible,
                    Profile = ContextProfile.Core,
                    ClientSize = new(RenderSize, RenderSize),
                    StartVisible = false,
                    StartFocused = false,
                };

                var window = new NativeWindow(settings);
                var rendererContext = new RendererContext(fileLoader, Logging.RendererLogger);

                window.MakeCurrent();

                GLEnvironment.Initialize(rendererContext.Logger);
                GLEnvironment.SetDefaultRenderState(rendererContext);

                var renderer = new Renderer(rendererContext);

                var textRenderer = new TextRenderer(rendererContext, renderer.Camera);
                textRenderer.Load();

                renderer.Postprocess.Load(4);

                var framebuffer = Framebuffer.Prepare("IconFramebuffer", RenderSize, RenderSize, 4,
                    ImageFormat.RGBA16161616F, ImageFormat.D16);
                framebuffer.Initialize();

                renderer.Initialize();
                renderer.MainFramebuffer = framebuffer;
                renderer.LoadRendererResources();

                LoadLighting(renderer);

                return new ModelIconRenderer(window, rendererContext, renderer, framebuffer, textRenderer);
            }
            catch (Exception ex)
            {
                Logging.Log($"Could not start the model renderer, entities with model icons will not get one: {ex.Message}", ConsoleColor.Red);
                return null;
            }
        }

        private static void LoadLighting(Renderer renderer)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WikiPageTools.industrial_sunset_puresky.vtex_c");
            Debug.Assert(stream != null, "The lighting cubemap is missing from the build.");

            using var cubemap = new Resource { FileName = "vrf_default_cubemap.vtex_c" };
            cubemap.Read(stream);

            Renderer.LoadDefaultLighting(renderer.Scene, cubemap);

            renderer.Scene.PostProcessInfo.AddPostProcessVolume(new ScenePostProcessVolume(renderer.Scene)
            {
                HasBloom = true,
                IsMaster = true,
                BloomSettings = new BloomSettings
                {
                    BlendMode = BloomBlendType.BLOOM_BLEND_SCREEN,
                    BloomStartValue = 1,
                    ScreenBloomStrength = 0.584f,
                    BloomThreshold = 1.972f,
                    BloomThresholdWidth = 2.364f,
                },
            });
        }

        /// <summary>Renders a model to a png, or returns false when it has nothing to draw.</summary>
        public bool TryRenderToFile(Resource modelResource, string pngDiskPath)
        {
            if (modelResource.DataBlock is not Model model)
            {
                return false;
            }

            window.MakeCurrent();
            renderer.Scene.Clear();

            // set before anything is added, framing measures against the viewport's aspect
            renderer.Camera.SetViewportSize(RenderSize, RenderSize);

            var modelNode = new ModelSceneNode(renderer.Scene, model, isWorldPreview: true);
            renderer.Scene.Add(modelNode, true);

            // a model with no visible meshes may still have a collision hull worth showing
            PhysAggregateData? phys = null;

            if (modelNode.RenderableMeshes.Count == 0)
            {
                phys = model.GetEmbeddedPhys();

                if (phys == null)
                {
                    return false;
                }

                foreach (var physNode in PhysSceneNode.CreatePhysSceneNodes(renderer.Scene, phys, null))
                {
                    physNode.Enabled = true;
                    physNode.IsTranslucentRenderMode = false;
                    renderer.Scene.Add(physNode, false);
                }
            }

            // every node, not just the model's own, so nothing hangs outside the frame
            var bounds = renderer.Scene.AllNodes.Select(node => node.BoundingBox).Aggregate((a, b) => a.Union(b));
            var size = bounds.Size * (phys == null ? 1.5f : 1f);

            // sets the angle as well as the distance, so one model can never inherit another's view
            renderer.Camera.FrameObjectFromAngle(bounds.Center, size.X, size.Z, size.Y, CameraYaw, CameraPitch);

            renderer.Scene.Initialize();

            rendererContext.MaxTextureSize = RenderSize;
            GL.Viewport(0, 0, RenderSize, RenderSize);
            framebuffer.Resize(RenderSize, RenderSize);

            renderer.Update(new Scene.UpdateContext
            {
                Camera = renderer.Camera,
                TextRenderer = textRenderer,
                Timestep = 0,
            });

            // Draw the model onto nothing rather than onto the scene's sky, so the icon sits on the
            // page background the way the material icons do. DrawMainScene, not Render, for the
            // same reason, and the result goes to an RGBA target because the window's own buffer
            // has no alpha to keep.
            framebuffer.Bind(FramebufferTarget.Framebuffer);
            GL.ClearColor(0f, 0f, 0f, 0f);
            GL.Clear(framebuffer.ClearMask);

            renderer.DrawMainScene();

            saveBuffer ??= CreateSaveBuffer();
            saveBuffer.Resize(RenderSize, RenderSize);
            saveBuffer.BindAndClear();

            renderer.PostprocessRender(framebuffer, saveBuffer, flipY: true);

            GL.Flush();
            GL.Finish();

            // a model can have meshes and still draw nothing, when every one of them is culled or fully
            // transparent, and that must not be reported as an icon that exists
            return SavePng(saveBuffer, pngDiskPath);
        }

        private static Framebuffer CreateSaveBuffer()
        {
            var buffer = Framebuffer.Prepare("IconSaveBuffer", RenderSize, RenderSize, 0, ImageFormat.RGBA8888, null);

            buffer.ClearColor = new OpenTK.Mathematics.Color4(0, 0, 0, 0);
            buffer.Initialize();

            return buffer;
        }

        /// <summary>
        /// Writes the render out as an icon, or returns false when nothing was actually drawn.
        ///
        /// Framing a bounding box leaves a model taking up around half the frame, because a box seen
        /// at an angle projects much smaller than its corners suggest, and how much smaller depends
        /// on the model's proportions. So the drawn pixels are measured and scaled up to fill the
        /// icon instead, which also makes every icon sit consistently regardless of the model.
        /// </summary>
        private static bool SavePng(Framebuffer source, string pngDiskPath)
        {
            using var rendered = new SKBitmap(RenderSize, RenderSize, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            source.Bind(FramebufferTarget.ReadFramebuffer);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            GL.ReadPixels(0, 0, RenderSize, RenderSize, PixelFormat.Bgra, PixelType.UnsignedByte, rendered.GetPixels(out _));

            if (!TryGetDrawnBounds(rendered, out var drawn))
            {
                return false;
            }

            var margin = (int)(IconSize * 0.04f);
            var scale = (IconSize - margin * 2) / (float)Math.Max(drawn.Width, drawn.Height);
            var width = Math.Max(1, (int)MathF.Round(drawn.Width * scale));
            var height = Math.Max(1, (int)MathF.Round(drawn.Height * scale));

            using var cropped = new SKBitmap(drawn.Width, drawn.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            rendered.ExtractSubset(cropped, drawn);

            using var scaled = cropped.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
                new SKSamplingOptions(SKCubicResampler.Mitchell));

            using var icon = new SKBitmap(IconSize, IconSize, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using (var canvas = new SKCanvas(icon))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(scaled, (IconSize - width) / 2f, (IconSize - height) / 2f);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(pngDiskPath)!);

            using var data = icon.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(pngDiskPath);
            data.SaveTo(file);

            return true;
        }

        /// <summary>The pixels the render actually touched, or false when the frame came out empty.</summary>
        private static bool TryGetDrawnBounds(SKBitmap bitmap, out SKRectI bounds)
        {
            int minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1;

            // straight over the pixel buffer, GetPixel per pixel is far too slow at this size.
            // Bgra8888, so alpha is the fourth byte of each pixel
            var pixels = bitmap.GetPixelSpan();
            var stride = bitmap.RowBytes;

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = pixels.Slice(y * stride, bitmap.Width * 4);

                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (row[x * 4 + 3] <= AlphaFloor)
                    {
                        continue;
                    }

                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }
                }
            }

            bounds = SKRectI.Create(minX, minY, maxX - minX + 1, maxY - minY + 1);

            return maxX >= 0;
        }

        public void Dispose()
        {
            renderer.Dispose();
            rendererContext.Dispose();
            window.Dispose();
        }
    }
}
