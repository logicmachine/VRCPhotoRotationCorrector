using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WinForms = System.Windows.Forms;

namespace VRCPhotoRotationCorrector
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int INPUT_SIZE = 480;

        private InferenceSession _session = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private InferenceSession InitializeSession()
        {
            if(_session == null)
            {
                var path = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "Assets\\VRCPhotoRotationEstimator.onnx");
                _session = new InferenceSession(path);
            }
            return _session;
        }

        private void DirectorySelectButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinForms.FolderBrowserDialog();
            dialog.Description = "Select Photo Folder";
            if(dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                DirectoryTextBox.Text = dialog.SelectedPath;
            }
        }

        private unsafe float[] TransposeAndCast(byte[] src)
        {
            if(src.Length != INPUT_SIZE * INPUT_SIZE * 4)
            {
                string s = INPUT_SIZE.ToString();
                throw new ArgumentException("The length of src must be 4*" + s + "*" + s + ".");
            }
            var dst = new float[3 * INPUT_SIZE * INPUT_SIZE];
            for(int y = 0; y < INPUT_SIZE; ++y)
            {
                for (int x = 0; x < INPUT_SIZE; ++x)
                {
                    var offset = (y * INPUT_SIZE + x) * 4;
                    dst[0 * INPUT_SIZE * INPUT_SIZE + y * INPUT_SIZE + x] = src[offset + 0] / 255.0f;
                    dst[1 * INPUT_SIZE * INPUT_SIZE + y * INPUT_SIZE + x] = src[offset + 1] / 255.0f;
                    dst[2 * INPUT_SIZE * INPUT_SIZE + y * INPUT_SIZE + x] = src[offset + 2] / 255.0f;
                }
            }
            return dst;
        }

        private unsafe void Rotate90(float[] data)
        {
            for (int c = 0; c < 3; ++c)
            {
                var baseOffset = c * INPUT_SIZE * INPUT_SIZE;
                for (int y = 0; y < INPUT_SIZE / 2; ++y)
                {
                    for (int x = 0; x < INPUT_SIZE / 2; ++x)
                    {
                        var yi = INPUT_SIZE - 1 - y;
                        var xi = INPUT_SIZE - 1 - x;
                        var offset0 = y  * INPUT_SIZE + x;
                        var offset1 = x  * INPUT_SIZE + yi;
                        var offset2 = yi * INPUT_SIZE + xi;
                        var offset3 = xi * INPUT_SIZE + y;
                        var v0 = data[offset0 + baseOffset];
                        var v1 = data[offset1 + baseOffset];
                        var v2 = data[offset2 + baseOffset];
                        var v3 = data[offset3 + baseOffset];
                        data[offset1 + baseOffset] = v0;
                        data[offset2 + baseOffset] = v1;
                        data[offset3 + baseOffset] = v2;
                        data[offset0 + baseOffset] = v3;
                    }
                }
            }
        }

        private void Correct(InferenceSession session, string path)
        {
            var rawImage = new BitmapImage();
            using (var stream = File.OpenRead(path))
            {
                rawImage.BeginInit();
                rawImage.CacheOption = BitmapCacheOption.OnLoad;
                rawImage.StreamSource = stream;
                rawImage.EndInit();
            }
            var scale = INPUT_SIZE / (double)Math.Max(rawImage.PixelWidth, rawImage.PixelHeight);
            var scaledImage = new TransformedBitmap(rawImage, new ScaleTransform(scale, scale));
            var data = new byte[INPUT_SIZE * INPUT_SIZE * 4];
            var stride = INPUT_SIZE * 4;
            var offsetX = (INPUT_SIZE - scaledImage.PixelWidth)  / 2;
            var offsetY = (INPUT_SIZE - scaledImage.PixelHeight) / 2;
            scaledImage.CopyPixels(data, stride, offsetX * 4 + offsetY * stride);
            var source = TransposeAndCast(data);
            var dims = new int[] { 1, 3, INPUT_SIZE, INPUT_SIZE };
            var probs = new float[4];
            for(int rot = 0; rot < 4; ++rot)
            {
                if(rot != 0) { Rotate90(source); }
                var tensor = new DenseTensor<float>(source, dims);
                var inputs = new List<NamedOnnxValue>() { NamedOnnxValue.CreateFromTensor<float>("input.1", tensor) };
                using (var results = session.Run(inputs))
                {
                    foreach(var r in results)
                    {
                        var values = r.AsTensor<float>().ToArray();
                        var e0 = (float)Math.Exp(values[0]);
                        var e1 = (float)Math.Exp(values[1]);
                        probs[rot] = e0 / (e0 + e1);
                    }
                }
            }
            var maxval = probs.Max();
            if(probs[0] != maxval)
            {
                var angle = 0.0;
                for(int a = 0; a < 4; ++a)
                {
                    if(probs[a] == maxval) { angle = a * 90.0; }
                }
                var rotatedImage = new TransformedBitmap(rawImage, new RotateTransform(angle));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rotatedImage));
                var tmp = path + ".tmp.png";
                using (var stream = File.OpenWrite(tmp))
                {
                    encoder.Save(stream);
                }
                File.Delete(path);
                File.Move(tmp, path);
            }
            // MessageBox.Show(path + ": " + probs[0].ToString() + ", " + probs[1].ToString() + ", " + probs[2].ToString() + ", " + probs[3].ToString());
        }

        private async void CorrectButton_Click(object sender, RoutedEventArgs e)
        {
            var directory = DirectoryTextBox.Text;
            if (!Directory.Exists(directory))
            {
                MessageBox.Show("Folder '" + directory + "' not found");
                return;
            }
            var session = InitializeSession();
            var paths = Directory.EnumerateFiles(directory)
                .Where(s => s.EndsWith(".png"))
                .ToList();
            CorrectionProgress.Minimum = 0;
            CorrectionProgress.Maximum = paths.Count;
            CorrectButton.IsEnabled = false;
            for (int i = 0; i < paths.Count; ++i)
            {
                await Task.Run(() =>
                {
                    Correct(session, paths[i]);
                });
                CorrectionProgress.Value = i + 1;
            }
            CorrectButton.IsEnabled = true;
        }
    }
}
