using ImageMagick;
using Newtonsoft.Json;
using QRCoder;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SocketIOClient;
using SocketIOClient.Common;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static Watch_Face_Editor.Form1;
using Device = SharpDX.Direct3D11.Device;

namespace Watch_Face_Editor
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        private SocketIO socket;
        private const int previewId = 1;
        private const string previewMethod = "ide.simulator.preview";
        IntPtr hWnd_ModelEmulator; // Дескриптор окна эмулятора

        private Rectangle captureArea = new Rectangle(0, 0, 480, 480);// Область захвата
        private CancellationTokenSource cts;
        private IScreenCapture capture;

        Bitmap currentFrame; // Временное хранение текущего кадра
        private readonly object frameLock = new object();

        public interface IScreenCapture
        {
            void Start(Rectangle area);
            void Stop();
            Bitmap GetFrame();
            bool IsSupported();
        }

        int EmulatorAreaPosX = 0;
        int EmulatorAreaPosY = 0;
        int EmulatorAreaWidth = 0;
        int EmulatorAreaHeight = 0;
        int EmulatorAreaOffsetX = 0;
        int EmulatorAreaOffsetY = 0;

        # region Файлообменники

        private async void btnUploadToLitterbox_Click(object sender, EventArgs e)
        {
            string zpkName = "";
            ; if (ProjectDir == null || Watch_Face == null)
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = Properties.FormStrings.FilterZpk;
                openFileDialog.RestoreDirectory = true;
                openFileDialog.Multiselect = false;
                //openFileDialog.Title = Properties.FormStrings.Dialog_Title_Unpack;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    zpkName = openFileDialog.FileName;
                    NameForQR = Path.GetFileNameWithoutExtension(zpkName);
                }
            }
            else
            {
                string SaveFileName = Path.GetFileNameWithoutExtension(FileName);
                if (Watch_Face.WatchFace_Info.WatchFaceName != null && Watch_Face.WatchFace_Info.WatchFaceName != "")
                    SaveFileName = Watch_Face.WatchFace_Info.WatchFaceName;
                zpkName = ProjectDir + @"\" + SaveFileName + ".zpk";
                string zipName = ProjectDir + @"\" + SaveFileName + ".zip";

                if (File.Exists(zpkName))
                {
                    DialogResult dr = MessageBox.Show(Properties.FormStrings.Message_Warning_OverwriteFile_Text,
                            Properties.FormStrings.Message_Warning_Caption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                    if (dr == DialogResult.Yes)
                    {
                        zipName = await CreateWatchFace(zipName);
                        if (File.Exists(zipName)) zpkName = CreateZPK(zipName);
                    }
                    if (dr == DialogResult.Cancel)
                    {
                        return;
                    }
                }
                else
                {
                    zipName = await CreateWatchFace(zipName);
                    if (File.Exists(zipName)) zpkName = CreateZPK(zipName);
                }
            }
            progressBar1.Visible = false;

            if (File.Exists(zpkName))
            {
                resetQRCode();

                try
                {
                    //string url = UploadToLitterbox(zpkName);
                    string url = await UploadToLitterboxAsync(zpkName);
                    textBox_LitterboxURL.Text = url.Replace("https://", "").Replace("http://", "");

                    string correctedURL = CorrectUrl(url);
                    //CheckFileExists(correctedURL);

                    //textBox_LitterboxURL.Text = correctedURL;

                    Bitmap currentQRCode = GenerateQRCodeWithLogo(correctedURL);
                    pictureBoxQRCode.Image = currentQRCode;
                    pictureBoxQRCode.Visible = true;
                    button_saveQR.Enabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Upload error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );

                }
            }
        }

        private async void btnUploadToFilePost_Click(object sender, EventArgs e)
        {
            string apiKey = textBox_FilePost_API_key.Text;
            bool isApiKeyValid = await CheckFilePostApiKeyAsync(apiKey);

            if (!isApiKeyValid)
            {
                MessageBox.Show(
                    Properties.FormStrings.Message_Warning_InvalidAPIKey_Text,
                    Properties.FormStrings.Message_Warning_Caption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            string zpkName = "";
            if (ProjectDir == null || Watch_Face == null)
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = Properties.FormStrings.FilterZpk;
                openFileDialog.RestoreDirectory = true;
                openFileDialog.Multiselect = false;
                //openFileDialog.Title = Properties.FormStrings.Dialog_Title_Unpack;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    zpkName = openFileDialog.FileName;
                    NameForQR = Path.GetFileNameWithoutExtension(zpkName);
                }
            }
            else
            {
                if (ProjectDir == null || Watch_Face == null) return;
                string SaveFileName = Path.GetFileNameWithoutExtension(FileName);
                if (Watch_Face.WatchFace_Info.WatchFaceName != null && Watch_Face.WatchFace_Info.WatchFaceName != "")
                    SaveFileName = Watch_Face.WatchFace_Info.WatchFaceName;
                zpkName = ProjectDir + @"\" + SaveFileName + ".zpk";
                string zipName = ProjectDir + @"\" + SaveFileName + ".zip";

                if (File.Exists(zpkName))
                {
                    DialogResult dr = MessageBox.Show(Properties.FormStrings.Message_Warning_OverwriteFile_Text,
                            Properties.FormStrings.Message_Warning_Caption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                    if (dr == DialogResult.Yes)
                    {
                        zipName = await CreateWatchFace(zipName);
                        if (File.Exists(zipName)) zpkName = CreateZPK(zipName);
                    }
                    if (dr == DialogResult.Cancel)
                    {
                        return;
                    }
                }
                else
                {
                    zipName = await CreateWatchFace(zipName);
                    if (File.Exists(zipName)) zpkName = CreateZPK(zipName);
                }
            }
            progressBar1.Visible = false;

            if (File.Exists(zpkName))
            {
                resetQRCode();

                try
                {
                    //string url = UploadToLitterbox(zpkName);
                    string url = await UploadToFilePostAsync(zpkName, apiKey);
                    textBox_LitterboxURL.Text = url.Replace("https://", "").Replace("http://", "");

                    string correctedURL = CorrectUrl(url);
                    //CheckFileExists(correctedURL);

                    //textBox_LitterboxURL.Text = correctedURL;

                    Bitmap currentQRCode = GenerateQRCodeWithLogo(correctedURL);
                    pictureBoxQRCode.Image = currentQRCode;
                    pictureBoxQRCode.Visible = true;
                    button_saveQR.Enabled = true;
                }
                catch (Exception ex)
                {

                    //pictureBoxQRCode.Image = GetLogo();
                    //label_URL_status.Text = "❌";
                    //label_URL_status.ForeColor = Color.Red;
                    //pictureBoxQRCode.Visible = false;
                    //button_saveQR.Enabled = false;

                    MessageBox.Show(
                        ex.Message,
                        "Upload error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );

                }

                panel_FilePost_info.Visible = true;
                try
                {
                    FilePostAccountInfo info = await GetFilePostAccountAsync(apiKey);

                    panel_FilePost_info.Visible = true;
                    if (info.email != null) label_FilePost_info_email.Text = "E-mail: " + info.email;

                    if (info.usage != null)
                    {
                        if (info.usage.uploads_this_month != 0 && info.usage.uploads_limit != 0)
                        {
                            string uploads_this_month = ": " + info.usage.uploads_this_month.ToString() + " / " +
                                info.usage.uploads_limit.ToString() + " (" +
                                (Math.Round(100f * info.usage.uploads_this_month / info.usage.uploads_limit, 1)).ToString() + "%)";
                            label_FilePost_info_uploads_month.Text = Properties.FormStrings.FilePost_info_uploads_month + uploads_this_month;
                        }


                        //string storage_used = ": " + info.usage.storage_used_mb.ToString() + " / " +
                        //    (Math.Round(100f * info.usage.storage_used_mb / info.usage.storage_limit_mb)).ToString() + " MB";
                        //if (info.usage.storage_used_mb != 0) label_FilePost_info_storage_used.Text = Properties.FormStrings.FilePost_info_storage_used + storage_used;
                    }
                }
                catch (Exception ex)
                {
                    label_FilePost_info_email.Text = "E-mail: -";
                    label_FilePost_info_uploads_month.Text = Properties.FormStrings.FilePost_info_uploads_month + ": -";
                    label_FilePost_info_storage_used.Text = Properties.FormStrings.FilePost_info_storage_used + ": -";
                    MessageBox.Show(
                        ex.Message,
                        "Get account info error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }

        private void resetQRCode()
        {
            pictureBoxQRCode.Visible = false;
            pictureBoxQRCode.Image = null;
            textBox_LitterboxURL.Text = "";
            //label_URL_status.Text = "❌";
            //label_URL_status.ForeColor = Color.Red;
            button_saveQR.Enabled = false;
            button_saveQR.Enabled = false;
        }

        public async Task<string> UploadToLitterboxAsync(string filePath)
        {
            string timeString = "1h";

            if (radioButton_12hours.Checked) timeString = "12h";
            else if (radioButton_1day.Checked) timeString = "24h";
            else if (radioButton_3days.Checked) timeString = "72h";
            NameValueCollection values = new NameValueCollection();

            values["reqtype"] = "fileupload";
            values["time"] = timeString;
            values["fileNameLength"] = "16";

            NameValueCollection headers = new NameValueCollection();

            headers["Accept"] = "text/plain, */*";
            headers["Origin"] = "https://litterbox.catbox.moe";
            headers["Referer"] = "https://litterbox.catbox.moe/";

            byte[] responseBytes =
                await UploadMultipartAsync(
                    "https://litterbox.catbox.moe/resources/internals/api.php",
                    filePath,
                    "fileToUpload",
                    values,
                    headers);

            string response = Encoding.UTF8.GetString(responseBytes).Trim();

            return response;
        }

        public async Task<string> UploadToFilePostAsync(string filePath, string apiKey)
        {
            NameValueCollection headers = new NameValueCollection();

            headers["X-API-Key"] = apiKey;

            byte[] responseBytes =
                await UploadMultipartAsync(
                    "https://filepost.dev/v1/upload",
                    filePath,
                    "file",
                    null,
                    headers);

            string response =
                Encoding.UTF8.GetString(responseBytes);

            JavaScriptSerializer serializer = new JavaScriptSerializer();

            dynamic json = serializer.Deserialize<dynamic>(response);

            if (json.ContainsKey("url")) return json["url"];

            throw new Exception("Invalid server response: " + response);
        }

        private async Task<byte[]> UploadMultipartAsync(
            string url,
            string filePath,
            string fileFieldName,
            NameValueCollection values,
            NameValueCollection headers = null)
        {
            using (WebClient client = new WebClient())
            {
                string boundary =
                    "---------------------------" +
                    DateTime.Now.Ticks.ToString("x");

                client.Headers.Add(
                    "Content-Type",
                    "multipart/form-data; boundary=" + boundary);

                client.Headers.Add(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                // Дополнительные headers
                if (headers != null)
                {
                    foreach (string key in headers.AllKeys)
                    {
                        client.Headers[key] = headers[key];
                    }
                }

                // ProgressBar
                client.UploadProgressChanged += (s, e) =>
                {
                    int progress = 0;
                    progressBar1.Minimum = 0;
                    progressBar1.Maximum = 100;
                    progressBar1.Value = 0;

                    if (e.TotalBytesToSend > 0)
                    {
                        progress = (int)(
                            (e.BytesSent * 100L) /
                            e.TotalBytesToSend);
                    }

                    if (progress < 0)
                        progress = 0;

                    if (progress > 100)
                        progress = 100;

                    if (progressBar1.InvokeRequired)
                    {
                        progressBar1.Invoke(new Action(() =>
                        {
                            progressBar1.Value = progress;
                        }));
                    }
                    else
                    {
                        progressBar1.Value = progress;
                    }
                };

                using (MemoryStream stream = new MemoryStream())
                {
                    // Текстовые поля
                    if (values != null)
                    {
                        foreach (string key in values.AllKeys)
                        {
                            byte[] part = Encoding.UTF8.GetBytes(
                                "--" + boundary + "\r\n" +
                                "Content-Disposition: form-data; name=\"" +
                                key + "\"\r\n\r\n" +
                                values[key] + "\r\n");

                            await stream.WriteAsync(part, 0, part.Length);
                        }
                    }

                    // Заголовок файла
                    string fileName = Path.GetFileName(filePath);

                    byte[] fileHeader = Encoding.UTF8.GetBytes(
                        "--" + boundary + "\r\n" +
                        "Content-Disposition: form-data; name=\"" +
                        fileFieldName +
                        "\"; filename=\"" +
                        fileName +
                        "\"\r\n" +
                        "Content-Type: application/octet-stream\r\n\r\n");

                    await stream.WriteAsync(
                        fileHeader,
                        0,
                        fileHeader.Length);

                    // Файл
                    using (FileStream fs = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read))
                    {
                        await fs.CopyToAsync(stream);
                    }

                    // Завершение multipart
                    byte[] trailer = Encoding.UTF8.GetBytes(
                        "\r\n--" + boundary + "--\r\n");

                    await stream.WriteAsync(
                        trailer,
                        0,
                        trailer.Length);

                    // Сброс progressbar
                    progressBar1.Value = 0;

                    // Upload async
                    return await client.UploadDataTaskAsync(
                        new Uri(url),
                        "POST",
                        stream.ToArray());
                }
            }
        }

        private string CorrectUrl(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // если начинается с https:// или http:// — заменяем на zpkd1://
            if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return input.Replace("https://", "zpkd1://")
                            .Replace("http://", "zpkd1://");
            }

            // если вообще не начинается ни с zpkd1://, ни с http(s)
            if (!input.StartsWith("zpkd1://", StringComparison.OrdinalIgnoreCase))
            {
                return "zpkd1://" + input;
            }

            return input;
        }

        private Bitmap GenerateQRCodeWithLogo(string text)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            {
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
                QRCode qrCode = new QRCode(qrCodeData);
                Bitmap qrRaw = qrCode.GetGraphic(10, Color.Black, Color.White, GetLogo(), 20, 5, false);
                Bitmap qrCodeImage = AddQuietZone(qrRaw, quietZoneModules: 2, pixelsPerModule: 10);
                return qrCodeImage;
            }
        }
        private Bitmap AddQuietZone(Bitmap originalQr, int quietZoneModules, int pixelsPerModule)
        {
            int quietZonePixels = quietZoneModules * pixelsPerModule;

            int newSize = originalQr.Width + quietZonePixels * 2;
            Bitmap result = new Bitmap(newSize, newSize);

            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.White); // цвет тихой зоны
                g.DrawImage(originalQr, quietZonePixels, quietZonePixels);
            }

            return result;
        }

        private Bitmap GetLogo()
        {
            // Предполагается, что logo.png добавлен в ресурсы проекта (Properties > Resources)
            return Properties.Resources.logo_qr;
        }


        private Bitmap DrawTextWithOptions(Bitmap image, string text, string position, int fontSize, int textAreaHeight, Color backgroundColor)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new Bitmap(image); // Без текста — просто копия

            int padding = 10;
            int width = image.Width;
            int newHeight = image.Height + textAreaHeight;

            //Font font = new Font("Calibri", fontSize, FontStyle.Regular);
            Font font = GetSafeFont("Calibri", fontSize, FontStyle.Regular);
            Bitmap result = new Bitmap(width, newHeight);

            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(backgroundColor);
                //g.Clear(Color.White);

                Rectangle textRect;
                Point imagePos;

                if (position.ToLower() == "top")
                {
                    // Текст сверху
                    textRect = new Rectangle(0, 0, width, textAreaHeight);
                    imagePos = new Point(0, textAreaHeight);
                }
                else
                {
                    // Текст снизу (по умолчанию)
                    textRect = new Rectangle(0, image.Height, width, textAreaHeight);
                    imagePos = new Point(0, 0);
                }

                // Отрисовка фона под текстом
                using (Brush bgBrush = new SolidBrush(backgroundColor))
                {
                    g.FillRectangle(bgBrush, textRect);
                }

                // Обрезка текста при необходимости
                string fittedText = FitTextToWidth(g, text, font, width - padding * 2);

                // Рисуем изображение
                g.DrawImage(image, imagePos);

                // Рисуем текст
                using (Brush textBrush = new SolidBrush(Color.Black))
                using (StringFormat format = new StringFormat()
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.DrawString(fittedText, font, textBrush, textRect, format);
                }
            }

            return result;
        }

        private Bitmap DrawTextWrapped(Bitmap image, string text, string position, int fontSize, Color backgroundColor, int minTextAreaHeight)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new Bitmap(image);

            Font font = GetSafeFont("Calibri", fontSize, FontStyle.Regular);
            int width = image.Width;
            int textPadding = 10;

            // Определяем высоту текста с переносами
            SizeF textSize;
            using (Bitmap dummyBmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(dummyBmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                RectangleF layout = new RectangleF(0, 0, width - textPadding * 2, float.MaxValue);
                textSize = g.MeasureString(text, font, layout.Size.ToSize(), new StringFormat());
            }

            int calculatedTextHeight = (int)Math.Ceiling(textSize.Height) + textPadding * 2;
            int textAreaHeight = Math.Max(calculatedTextHeight, minTextAreaHeight);

            int newHeight = image.Height + textAreaHeight;
            Bitmap result = new Bitmap(width, newHeight);

            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                //g.Clear(Color.White);
                g.Clear(backgroundColor);

                Rectangle textRect;
                Point imagePos;

                if (position.ToLower() == "top")
                {
                    textRect = new Rectangle(0, 0, width, textAreaHeight);
                    imagePos = new Point(0, textAreaHeight);
                }
                else
                {
                    textRect = new Rectangle(0, image.Height, width, textAreaHeight);
                    imagePos = new Point(0, 0);
                }

                // Фон текста
                using (Brush bgBrush = new SolidBrush(backgroundColor))
                {
                    g.FillRectangle(bgBrush, textRect);
                }

                // Рисуем изображение
                g.DrawImage(image, imagePos);

                // Рисуем текст с переносом
                using (Brush textBrush = new SolidBrush(Color.Black))
                using (StringFormat format = new StringFormat()
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Near,
                    FormatFlags = StringFormatFlags.LineLimit
                })
                {
                    Rectangle paddedRect = new Rectangle(textRect.X + textPadding, textRect.Y + textPadding,
                                                         textRect.Width - textPadding * 2,
                                                         textRect.Height - textPadding * 2);
                    g.DrawString(text, font, textBrush, paddedRect, format);
                }
            }

            return result;
        }

        private Font GetSafeFont(string preferredFontName, float fontSize, FontStyle style, string fallbackFontName = "Segoe UI")
        {
            using (InstalledFontCollection fonts = new InstalledFontCollection())
            {
                bool hasPreferred = fonts.Families.Any(f => f.Name.Equals(preferredFontName, StringComparison.OrdinalIgnoreCase));

                string fontToUse = hasPreferred ? preferredFontName : fallbackFontName;
                return new Font(fontToUse, fontSize, style);
            }
        }

        private string FitTextToWidth(Graphics g, string text, Font font, int maxWidth)
        {
            const string ellipsis = "...";

            if (string.IsNullOrEmpty(text))
                return "";

            // Если весь текст помещается — возвращаем его
            if (g.MeasureString(text, font).Width <= maxWidth)
                return text;

            // Подрезаем посимвольно
            for (int i = text.Length - 1; i > 0; i--)
            {
                string cut = text.Substring(0, i).Trim() + ellipsis;
                if (g.MeasureString(cut, font).Width <= maxWidth)
                    return cut;
            }

            // Если даже один символ + "..." не влезает — возвращаем "..."
            return ellipsis;
        }

        private Bitmap ApplyRoundedCorners(Bitmap original, int cornerRadius)
        {
            Bitmap rounded = new Bitmap(original.Width, original.Height);
            using (Graphics g = Graphics.FromImage(rounded))
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, original.Width, original.Height), cornerRadius))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (Brush imageBrush = new TextureBrush(original))
                {
                    g.FillPath(imageBrush, path);
                }
            }

            return rounded;
        }

        private void DrawRoundedBorder(Graphics g, int width, int height, int cornerRadius, int borderWidth, Color borderColor)
        {
            using (Pen pen = new Pen(borderColor, borderWidth))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                pen.Alignment = PenAlignment.Center;
                int d = cornerRadius * 2;

                Rectangle topLeftArc = new Rectangle(0, 0, d, d);
                Rectangle topRightArc = new Rectangle(width - d, 0, d, d);
                Rectangle bottomRightArc = new Rectangle(width - d, height - d, d, d);
                Rectangle bottomLeftArc = new Rectangle(0, height - d, d, d);

                // Углы
                g.DrawArc(pen, topLeftArc, 180, 90);
                g.DrawArc(pen, topRightArc, 270, 90);
                g.DrawArc(pen, bottomRightArc, 0, 90);
                g.DrawArc(pen, bottomLeftArc, 90, 90);

                // Прямые линии между углами
                g.DrawLine(pen, cornerRadius, 0, width - cornerRadius, 0);               // Верх
                g.DrawLine(pen, width, cornerRadius, width, height - cornerRadius);      // Право
                g.DrawLine(pen, width - cornerRadius, height, cornerRadius, height);     // Низ
                g.DrawLine(pen, 0, height - cornerRadius, 0, cornerRadius);              // Лево
            }
        }

        private GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();

            // 4 дуги
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);

            path.CloseFigure();
            return path;
        }

        private Bitmap PadImageWithMargins(Bitmap original, int marginLeft, int marginTop, int marginRight, int marginBottom, Color backgroundColor)
        {
            int newWidth = original.Width + marginLeft + marginRight;
            int newHeight = original.Height + marginTop + marginBottom;

            Bitmap padded = new Bitmap(newWidth, newHeight);
            using (Graphics g = Graphics.FromImage(padded))
            {
                g.Clear(backgroundColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                g.DrawImage(original, marginLeft, marginTop, original.Width, original.Height);
            }

            return padded;
        }

        public static async Task<bool> UrlFileExistsAsync(string url)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);

                    using (var response = await client.SendAsync(
                        new HttpRequestMessage(HttpMethod.Head, url)))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        //private async void CheckFileExists(string url)
        //{
        //    url = url.Replace("zpkd1://", "http://");
        //    bool exists = await UrlFileExistsAsync(url);

        //    //label_URL_status.Text = exists ? "Файл найден ✅" : "Файл не найден ❌";
        //    if (exists)
        //    {
        //        label_URL_status.Text = "✅";
        //        label_URL_status.ForeColor = Color.Green;
        //    }
        //    else
        //    {
        //        label_URL_status.Text = "⚠️";
        //        label_URL_status.ForeColor = Color.DarkOrange;
        //        //label_URL_status.Text = "❌";
        //        //label_URL_status.ForeColor = Color.Red;
        //        //pictureBoxQRCode.Visible = false;
        //        //button_saveQR.Enabled = false;
        //        //resetQRCode();
        //    }
        //}

        public async Task<bool> CheckFilePostApiKeyAsync(string apiKey)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add(
                        "User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                    client.Headers.Add("X-API-Key", apiKey);

                    byte[] responseBytes = await client.DownloadDataTaskAsync("https://filepost.dev/v1/account");

                    string response = Encoding.UTF8.GetString(responseBytes);

                    // если дошли сюда и получили JSON → ключ валиден
                    return true;
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse response)
                {
                    // стандартные ошибки авторизации
                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        return false;
                    }

                    // другие ошибки (500, network и т.д.)
                    throw;
                }

                throw;
            }
        }

        public async Task<FilePostAccountInfo> GetFilePostAccountAsync(string apiKey)
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                client.Headers.Add("X-API-Key", apiKey);

                byte[] responseBytes = await client.DownloadDataTaskAsync("https://filepost.dev/v1/account");

                string json = Encoding.UTF8.GetString(responseBytes);

                JavaScriptSerializer serializer = new JavaScriptSerializer();

                return serializer.Deserialize<FilePostAccountInfo>(json);
            }
        }

        private void button_saveQR_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                string nameText = NameForQR;
                if (FileName != null) Path.GetFileNameWithoutExtension(FileName);
                if (Watch_Face != null && Watch_Face.WatchFace_Info != null)
                {
                    if (Watch_Face.WatchFace_Info.WatchFaceName != null && Watch_Face.WatchFace_Info.WatchFaceName != "")
                        nameText = Watch_Face.WatchFace_Info.WatchFaceName;
                    nameText = nameText.Trim();
                    if (Watch_Face.WatchFace_Info.WatchFaceVersion != 0) nameText += " v" + Watch_Face.WatchFace_Info.WatchFaceVersion.ToString() + ".0.0";
                }
                string fileName = "QRCode.png";
                if (!string.IsNullOrWhiteSpace(nameText)) fileName = "QRCode " + nameText + ".png";
                fileName = fileName.Replace(' ', '_');

                saveDialog.Filter = "PNG Image|*.png";
                saveDialog.Title = "Save QR code";
                saveDialog.FileName = fileName;

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    bool topText = false;
                    bool bottomText = false;

                    // Добавляем обводку QR-кода
                    Bitmap currentQRCode = pictureBoxQRCode.Image as Bitmap;
                    using (Graphics g = Graphics.FromImage(currentQRCode))
                    {
                        DrawRoundedBorder(g, currentQRCode.Width, currentQRCode.Height, cornerRadius: 15, borderWidth: 4, borderColor: Color.Black);
                    }
                    Bitmap roundedQRCode = ApplyRoundedCorners(currentQRCode, cornerRadius: 15);

                    //int textAreaHeight = 30;
                    //if (!string.IsNullOrWhiteSpace(nameText)) textAreaHeight = 25;
                    //Bitmap imageWithType = DrawTextWithOptions(
                    //    roundedQRCode,
                    //    text: type,
                    //    position: "top",
                    //    fontSize: 16,
                    //    textAreaHeight: textAreaHeight,
                    //    backgroundColor: Color.FromArgb(255, 255, 224, 178)
                    //);

                    // Имя и версия приложения
                    Bitmap imageWithName = DrawTextWithOptions(
                        roundedQRCode,
                        text: nameText,
                        position: "top",
                        fontSize: 24,
                        textAreaHeight: 40,
                        backgroundColor: Color.FromArgb(255, 255, 224, 178)
                    );
                    if (!string.IsNullOrWhiteSpace(nameText))
                        imageWithName = PadImageWithMargins(imageWithName, 0, 5, 0, 0, Color.FromArgb(255, 255, 224, 178));
                    if (imageWithName.Height > currentQRCode.Height) topText = true;

                    string modelsText = ProgramSettings.Watch_Model;
                    if (SelectedModel.screenType == "round") modelsText = "○ " + modelsText;
                    else if (SelectedModel.screenType == "square") modelsText = "▢ " + modelsText;
                    else if (SelectedModel.screenType == "bar") modelsText = "▯ " + modelsText;
                    //Bitmap imageWithModels = DrawTextWrapped(
                    Bitmap imageWithModels = DrawTextWithOptions(
                            imageWithName,
                            text: modelsText,
                            position: "bottom",
                            fontSize: 24,
                            textAreaHeight: 40,
                            backgroundColor: Color.FromArgb(255, 255, 224, 178)
                        );
                    if (imageWithModels.Height > imageWithName.Height) bottomText = true;

                    if (imageWithModels.Height > currentQRCode.Height)
                    {
                        int marginLeft = 0;
                        int marginTop = 0;
                        int marginRight = 0;
                        int marginBottom = 0;

                        if (topText && bottomText)
                        {
                            marginLeft = 30;
                            marginRight = 30;
                        }
                        else if (topText && !bottomText)
                        {
                            marginLeft = 20;
                            marginRight = 20;
                            marginBottom = 20;
                        }
                        else if (!topText && bottomText)
                        {
                            marginLeft = 20;
                            marginRight = 20;
                            marginTop = 20;
                        }

                        Bitmap finalSize = PadImageWithMargins(imageWithModels, marginLeft, marginTop, marginRight, marginBottom, Color.FromArgb(255, 255, 224, 178));
                        // Добавляем обводку всего изображения
                        using (Graphics g = Graphics.FromImage(finalSize))
                        {
                            DrawRoundedBorder(g, finalSize.Width, finalSize.Height, cornerRadius: 30, borderWidth: 6, borderColor: Color.Black);
                        }
                        Bitmap finalSizeRounded = ApplyRoundedCorners(finalSize, cornerRadius: 30);
                        finalSizeRounded.Save(saveDialog.FileName, ImageFormat.Png);
                    }
                    else imageWithModels.Save(saveDialog.FileName, ImageFormat.Png);

                    if (File.Exists(saveDialog.FileName))
                    {
                        Process.Start("explorer.exe", $"/select,\"{saveDialog.FileName}\"");
                    }
                }
            }
        }

        private void textBox_FilePost_API_key_TextChanged(object sender, EventArgs e)
        {
            ProgramSettings.FilePost_API_key = textBox_FilePost_API_key.Text;
            if (textBox_FilePost_API_key.Text.Length > 10) btnUploadToFilePost.Enabled = true;
            else btnUploadToFilePost.Enabled = false;

            string JSON_String = JsonConvert.SerializeObject(ProgramSettings, Formatting.Indented, new JsonSerializerSettings
            {
                //DefaultValueHandling = DefaultValueHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(Application.StartupPath + @"\Settings.json", JSON_String, Encoding.UTF8);
        }

        private void radioButton_Litterbox_CheckedChanged(object sender, EventArgs e)
        {
            panel_Litterbox.Visible = radioButton_Litterbox.Checked;
            panel_FilePost.Visible = radioButton_FilePost.Checked;
            panel_FilePost.Location = panel_Litterbox.Location;
            if (radioButton_Litterbox.Checked) panel_FilePost_info.Visible = false;
        }
        #endregion

        #region Similator

        private void button_SimulatorConnect_Click(object sender, EventArgs e)
        {
            // Поиск окна симуляторапо заголовку
            IntPtr hWnd_Simulator = FindWindow(null, "Huami OS Simulator");
            if (hWnd_Simulator == IntPtr.Zero)
            {
                MessageBox.Show(Properties.FormStrings.Message_LaunchSimulator_Text, 
                    Properties.FormStrings.Message_Warning_Caption, 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Поиск окна эмулятора по типу и заголовку
            hWnd_ModelEmulator = FindWindow("gdkWindowToplevel", "Zepp OS Simulator");
            if (hWnd_ModelEmulator == IntPtr.Zero) 
                hWnd_ModelEmulator = FindWindow("gdkWindowToplevel", "Zepp OS Simulator - Press Ctrl+Alt+G to release grab");
            if (hWnd_ModelEmulator == IntPtr.Zero) hWnd_ModelEmulator = FindWindow("gdkWindowToplevel", null);
            if (hWnd_ModelEmulator == IntPtr.Zero)
            {
                MessageBox.Show(Properties.FormStrings.Message_SimulatorRuning_Text1 + Environment.NewLine + 
                    Properties.FormStrings.Message_SimulatorRuning_Text2, 
                    Properties.FormStrings.Message_Warning_Caption, 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            WinApiHelper.ClientAreaInfo EmulatorArea = WinApiHelper.GetClientArea(hWnd_ModelEmulator);

            if (socket == null)
            {
                SimulatorInit();
                return;
            }
            else if (!socket.Connected)
            {
                MessageBox.Show(
                    Properties.FormStrings.Message_Simulator_StillConnecting_Text, 
                    Properties.FormStrings.Message_Warning_Caption, 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        private async void button_SimulatorSend_Click(object sender, EventArgs e)
        {
            if (ProjectDir == null || Watch_Face == null)
            {
                MessageBox.Show(
                    Properties.FormStrings.Message_OpenProject_Text, 
                    Properties.FormStrings.Message_Warning_Caption, 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (socket == null || !socket.Connected)
            {
                MessageBox.Show(Properties.FormStrings.Message_Simulator_NotConnected_Text, 
                    Properties.FormStrings.Message_Warning_Caption, 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string zpkName = "";
            string SaveFileName = "TestInSimulator";
            //string tempDir = Application.StartupPath + @"\SimulatorTemp";
            string tempDir = Application.StartupPath + @"\Temp";
            zpkName = tempDir + @"\" + SaveFileName + ".zpk";
            string zipName = tempDir + @"\" + SaveFileName + ".zip";

            zipName = await CreateWatchFace(zipName, true);
            if (File.Exists(zipName)) zpkName = CreateZPK(zipName);

            progressBar1.Visible = false;

            if (File.Exists(zpkName) && Watch_Face != null && Watch_Face.WatchFace_Info != null)
            {
                byte[] zpkBuffer = ReadZpkFile(zpkName);
                int appId = 1234567;
                if (Watch_Face.WatchFace_Info.WatchFaceId != 0 ) appId = (int)Watch_Face.WatchFace_Info.WatchFaceId;
                string projectName = Path.GetFileNameWithoutExtension(FileName);
                if (Watch_Face.WatchFace_Info.WatchFaceName != null && Watch_Face.WatchFace_Info.WatchFaceName != "")
                    projectName = Watch_Face.WatchFace_Info.WatchFaceName;
                List<int> devSources = new List<int> {};
                foreach (int id in SelectedModel.deviceSource_ids)
                {
                    devSources.Add(id);
                }

                // Send the data to the simulator using the Upload method
                await Upload(zpkBuffer, projectName, "watch", appId, devSources);
                //MessageBox.Show("Push Sent!", "Message", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (WindowHelper.WindowExists(hWnd_ModelEmulator))
                {
                    WindowHelper.BringToFront(hWnd_ModelEmulator);
                }
            }
        }

        private void checkBox_screen_capture_CheckedChanged(object sender, EventArgs e)
        {
            panel_SimulatorCapture.Visible = checkBox_screen_capture.Checked;
        }

        private void button_UpdateScreenPos_Click(object sender, EventArgs e)
        {
            updateSimulatorPos();
        }

        private void updateSimulatorPos()
        {
            // Поиск окна симуляторапо заголовку
            IntPtr hWnd_Simulator = FindWindow(null, "Huami OS Simulator");
            if (hWnd_Simulator == IntPtr.Zero)
            {
                MessageBox.Show(Properties.FormStrings.Message_LaunchSimulator_Text,
                    Properties.FormStrings.Message_Warning_Caption,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Поиск окна эмулятора по типу и заголовку
            hWnd_ModelEmulator = FindWindow("gdkWindowToplevel", "Zepp OS Simulator");
            if (hWnd_ModelEmulator == IntPtr.Zero)
                hWnd_ModelEmulator = FindWindow("gdkWindowToplevel", "Zepp OS Simulator - Press Ctrl+Alt+G to release grab");
            if (hWnd_ModelEmulator == IntPtr.Zero) hWnd_ModelEmulator = FindWindow("gdkWindowToplevel", null);
            if (hWnd_ModelEmulator == IntPtr.Zero)
            {
                MessageBox.Show(Properties.FormStrings.Message_SimulatorRuning_Text1 + Environment.NewLine +
                    Properties.FormStrings.Message_SimulatorRuning_Text2,
                    Properties.FormStrings.Message_Warning_Caption,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (hWnd_ModelEmulator != IntPtr.Zero)
            {
                WinApiHelper.ClientAreaInfo EmulatorArea = WinApiHelper.GetClientArea(hWnd_ModelEmulator);
                EmulatorAreaPosX = EmulatorArea.WindowRect.Left + EmulatorArea.ClientOffset.X;
                EmulatorAreaPosY = EmulatorArea.WindowRect.Top + EmulatorArea.ClientOffset.Y;
                label_ScreenPosX.Text = "X: " + EmulatorAreaPosX.ToString();
                label_ScreenPosY.Text = "Y: " + EmulatorAreaPosY.ToString();
                //label_offsetX.Text = "OffsetX: " + EmulatorArea.ClientOffset.X.ToString();
                //label_offsetY.Text = "OffsetY: " + EmulatorArea.ClientOffset.Y.ToString();
                //label_Width.Text = "Width: " + EmulatorArea.Width.ToString();
                //label_Height.Text = "Height: " + EmulatorArea.Height.ToString();

                if (numericUpDown_CaptureHeight.Value <= 100)
                {
                    numericUpDown_CaptureHeight.Value = SelectedModel.background.h;
                    EmulatorAreaOffsetY = EmulatorArea.Height - SelectedModel.background.h;
                    EmulatorAreaOffsetX = (EmulatorArea.Width - SelectedModel.background.w) / 2;
                    numericUpDown_CaptureOffsetX.Value = EmulatorAreaOffsetX;
                    numericUpDown_CaptureOffsetY.Value = EmulatorAreaOffsetY;
                }
                captureArea.X = EmulatorAreaPosX + EmulatorAreaOffsetX;
                captureArea.Y = EmulatorAreaPosY + EmulatorAreaOffsetY;
                captureArea.Width = EmulatorAreaWidth;
                captureArea.Height = EmulatorAreaHeight;
            }
        }

        private void numericUpDown_CaptureHeight_ValueChanged(object sender, EventArgs e)
        {
            EmulatorAreaWidth = (int)Math.Round(numericUpDown_CaptureHeight.Value / SelectedModel.background.h * SelectedModel.background.w);
            EmulatorAreaHeight = (int)numericUpDown_CaptureHeight.Value;
            label_CaptureWidth.Text = EmulatorAreaWidth.ToString();

            captureArea.Width = EmulatorAreaWidth;
            captureArea.Height = EmulatorAreaHeight;
        }

        private void resetCaptureArea()
        {
            numericUpDown_CaptureHeight.Value = 99;
            updateSimulatorPos();
        }

        private void button_CaptureStart_Click(object sender, EventArgs e)
        {
            if (radioButtonDxgi.Checked)
            {
                var dxgi = new DxgiCapture();

                if (!dxgi.IsSupported())
                {
                    MessageBox.Show(Properties.FormStrings.Message_DXGI_error_Text);
                    capture = new BitBltCapture();
                    radioButtonBitBlt.Checked = true;
                }
                else
                {
                    capture = dxgi;
                }
            }
            else
            {
                capture = new BitBltCapture();
            }

            if (WindowHelper.WindowExists(hWnd_ModelEmulator))
            {
                WindowHelper.BringToFront(hWnd_ModelEmulator);
            }

            SwitchCaptureElements(false);

            StartCapture();
        }

        private void SwitchCaptureElements(bool enabled)
        {
            checkBox_screen_capture.Enabled = enabled;
            button_UpdateScreenPos.Enabled = enabled;
            numericUpDown_CaptureHeight.Enabled = enabled;
            numericUpDown_CaptureOffsetX.Enabled = enabled;
            numericUpDown_CaptureOffsetY.Enabled = enabled;
            button_CaptureStart.Enabled = enabled;
            button_CaptureStop.Enabled = !enabled;
            radioButtonDxgi.Enabled = enabled;
            radioButtonBitBlt.Enabled = enabled;
            button_CaptureSaveGif.Enabled = !enabled;
        }

        private void button_CaptureStop_Click(object sender, EventArgs e)
        {
            Console.WriteLine("button_CaptureStop");
            cts?.Cancel();
            Console.WriteLine("cts?.Cancel();");
            capture?.Stop();
            Console.WriteLine("capture?.Stop();");
            SwitchCaptureElements(true);
        }

        private void button_CaptureSaveGif_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            //openFileDialog.InitialDirectory = subPath;
            saveFileDialog.Filter = "GIF Files: (*.gif)|*.gif";
            saveFileDialog.FileName = "Preview.gif";
            //openFileDialog.Filter = "Binary File (*.bin)|*.bin";
            ////openFileDialog1.FilterIndex = 2;
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.Title = Properties.FormStrings.Dialog_Title_SaveGIF;
            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                numericUpDown_CaptureFrameCount.Enabled = false;

                progressBar_Capture.Value = 0;
                progressBar_Capture.Maximum = (int)numericUpDown_CaptureFrameCount.Value;
                progressBar_Capture.Visible = true;

                Task.Run(async () =>
                {
                    await Task.Delay(100); // 👈 небольшая пауза (UI успевает обновиться)
                    try
                    {
                        int delay = (int)(1000 / numericUpDown_CaptureFps.Value);
                        int frameCount = 0;
                        using (Bitmap mask = new Bitmap(Application.StartupPath + @"\Mask\" + SelectedModel.maskImage))
                        {
                            using (MagickImageCollection collection = new MagickImageCollection())
                            {
                                while (frameCount < numericUpDown_CaptureFrameCount.Value)
                                {
                                    Bitmap bitmap = null;

                                    lock (frameLock)
                                    {
                                        bitmap = (Bitmap)currentFrame.Clone();
                                    }

                                    //if (checkBox_WatchSkin_Use.Checked) bitmap = ApplyWatchSkin(bitmap);
                                    //else if (checkBox_crop.Checked) bitmap = ApplyMask(bitmap, mask);
                                    Bitmap processed = bitmap;

                                    if (checkBox_WatchSkin_Use.Checked) processed = ApplyWatchSkin(bitmap);
                                    else if (checkBox_crop.Checked) processed = ApplyMask(bitmap, mask);

                                    if (!ReferenceEquals(processed, bitmap)) bitmap.Dispose();
                                    bitmap = processed;

                                    // Add first image and set the animation delay to 100ms
                                    //MagickImage item = new MagickImage(ImgConvert.CopyImageToByteArray(bitmap));
                                    //collection.Add(item);
                                    using (var item = new MagickImage(ImgConvert.CopyImageToByteArray(bitmap)))
                                    {
                                        collection.Add(item.Clone());
                                    }
                                    collection[collection.Count - 1].AnimationDelay = (int)(100 / numericUpDown_CaptureFps.Value);
                                    bitmap.Dispose();
                                    //item.Dispose();

                                    frameCount++;
                                    Invoke(new Action(() =>
                                    {
                                        progressBar_Capture.Value = frameCount;
                                    }));
                                    await Task.Delay(delay);
                                }

                                // Optionally reduce colors
                                QuantizeSettings settings = new QuantizeSettings();
                                //settings.Colors = 256;
                                //collection.Quantize(settings);

                                // Optionally optimize the images (images should have the same size).
                                collection.OptimizeTransparency();
                                //collection.Optimize();

                                // Save gif
                                collection.Write(saveFileDialog.FileName);
                            } // using
                        } // using mask

                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString());
                    }
                    Invoke(new Action(() =>
                    {
                        progressBar_Capture.Visible = false;
                    }));
                });
                //progressBar_Capture.Visible = false;

                numericUpDown_CaptureFrameCount.Enabled = true;
            }
        }

        private void StartCapture()
        {
            cts = new CancellationTokenSource();

            capture.Start(captureArea);

            Task.Run(async () =>
            {
                int delay = (int)(1000 / numericUpDown_CaptureFps.Value);

                while (!cts.Token.IsCancellationRequested)
                {
                    var frame = capture.GetFrame();
                    if (frame != null)
                    {
                        Bitmap uiCopy = null;

                        lock (frameLock)
                        {
                            currentFrame?.Dispose();
                            currentFrame = (Bitmap)frame.Clone();
                            uiCopy = (Bitmap)currentFrame.Clone();
                        }

                        BeginInvoke(new Action(() =>
                        {
                            var old = pictureBox_Capture.Image;
                            pictureBox_Capture.Image = uiCopy;
                            old?.Dispose();
                        }));
                    }

                    await Task.Delay(delay);
                }
            }, cts.Token);
        }


        public void SimulatorInit()
        {
            string url = "http://localhost:7650";
            if (socket != null) return;

            // Setup SocketIO options based on the simulator's requirements
            SocketIOOptions options = new SocketIOOptions
            {
                EIO = EngineIO.V4,
                Transport = TransportProtocol.WebSocket,
            };
            socket = new SocketIO(new Uri(url), options);

            // Socket on message listener
            socket.On("message", ctx =>
            {
                try
                {
                    // 1. Read json strings
                    string jsonPayload = ctx.GetValue<string>(0);
                    Console.WriteLine($"[DEBUG] Simulator payload: {jsonPayload}");
                    // 2. Parse JSON
                    var response = System.Text.Json.JsonSerializer.Deserialize<SimulatorResponse>(jsonPayload);
                    // 3. Process the result
                    if (response?.result?.success == true)
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            MessageBox.Show(Properties.FormStrings.Message_Simulator_PushSuccess_Text, 
                                Properties.FormStrings.Message_Infirmation_Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        });
                    }
                    else
                    {
                        throw new Exception("Receives error message from the simulator");
                    }
                }
                catch (Exception ex)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        MessageBox.Show($"Error on processing message:\n{ex.Message}", Properties.FormStrings.Message_Error_Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
                return Task.CompletedTask;
            });

            // Socket on connect listener
            socket.OnConnected += (sender, e) =>
            {
                this.BeginInvoke((MethodInvoker)delegate {
                    MessageBox.Show(Properties.FormStrings.Message_ConnectedSimulator_Text, 
                        Properties.FormStrings.Message_Infirmation_Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    button_SimulatorSend.Enabled = true;
                    checkBox_screen_capture.Enabled = true;
                    updateSimulatorPos();
                });
            };

            socket.OnDisconnected += (sender, e) =>
            {
                this.BeginInvoke((MethodInvoker)delegate
                {
                    MessageBox.Show(Properties.FormStrings.Message_Simulator_Disconnected_Text,
                        Properties.FormStrings.Message_Warning_Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    button_SimulatorSend.Enabled = false;
                    checkBox_screen_capture.Checked = false;
                    checkBox_screen_capture.Enabled = false;
                });
            };

            // Connect to the simulator asynchronously
            Task.Run(async () => {
                try
                {
                    await socket.ConnectAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Connect Error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Read the ZPK file as a byte array to be sent to the simulator.
        /// </summary>
        public byte[] ReadZpkFile(string zpkPath)
        {
            string fullPath = Path.GetFullPath(zpkPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException($"ZPK file not found: {fullPath}");

            return File.ReadAllBytes(fullPath);
        }

        public async Task Upload(byte[] data, string projectName, string target, int appid, List<int> devices)
        {
            var dataArr = Array.ConvertAll(data, b => (int)b);

            // 1. Make a payload object that matches the simulator's expected structure
            var payload = new
            {
                jsonrpc = "2.0",
                method = previewMethod,
                @params = new
                {
                    target = target,
                    projectName = projectName,
                    appid = appid,
                    size = data.Length,
                    data = dataArr,
                    devices = devices
                },
                id = previewId
            };


            // 2. Serialize the payload to a JSON string using System.Text.Json
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null // Keep naming policy as the node.js version
            };
            string jsonMessage = System.Text.Json.JsonSerializer.Serialize(payload, options);

            Console.WriteLine($"[DEBUG] Send 'message' event!");

            // 4. Send the JSON string to the simulator use same method like the Node via Socket.IO
            // Uses new[] { jsonString } to matching IEnumerable<object> for EmitAsync
            await socket.EmitAsync("message", new[] { jsonMessage });
        }

        #endregion
    }

    # region Файлообменники
    public class FilePostAccountInfo
    {
        public string email { get; set; }

        public string plan { get; set; }

        public FilePostUsage usage { get; set; }
    }

    public class FilePostUsage
    {
        public int uploads_this_month { get; set; }

        public int uploads_limit { get; set; }

        public int max_file_size_mb { get; set; }
    }
    #endregion

    #region Simulator

    public static class WinApiHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        public struct ClientAreaInfo
        {
            public RECT WindowRect;     // всё окно (экранные координаты)
            public RECT ClientRect;     // клиентская область (экранные координаты)
            public POINT ClientOffset;  // смещение клиента внутри окна
            public int Width;
            public int Height;
        }

        public static ClientAreaInfo GetClientArea(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                throw new ArgumentException("Invalid handle");

            // 1. Окно (экранные координаты)
            GetWindowRect(hWnd, out RECT windowRect);

            // 2. Размер клиентской области (локальный)
            GetClientRect(hWnd, out RECT clientRectLocal);

            // 3. Конвертируем (0,0) клиента в экран
            POINT clientTopLeft = new POINT { X = 0, Y = 0 };
            ClientToScreen(hWnd, ref clientTopLeft);

            // 4. Конвертируем (W,H) клиента в экран
            POINT clientBottomRight = new POINT
            {
                X = clientRectLocal.Right,
                Y = clientRectLocal.Bottom
            };
            ClientToScreen(hWnd, ref clientBottomRight);

            // 5. Итоговый rect клиента в экранных координатах
            RECT clientRectScreen = new RECT
            {
                Left = clientTopLeft.X,
                Top = clientTopLeft.Y,
                Right = clientBottomRight.X,
                Bottom = clientBottomRight.Y
            };

            // 6. Смещение клиента внутри окна
            POINT offset = new POINT
            {
                X = clientTopLeft.X - windowRect.Left,
                Y = clientTopLeft.Y - windowRect.Top
            };

            return new ClientAreaInfo
            {
                WindowRect = windowRect,
                ClientRect = clientRectScreen,
                ClientOffset = offset,
                Width = clientRectScreen.Right - clientRectScreen.Left,
                Height = clientRectScreen.Bottom - clientRectScreen.Top
            };
        }
    }

    public class SimulatorResponse
    {
        public string jsonrpc { get; set; }
        public int id { get; set; }
        public SimulatorResult result { get; set; }
    }
    public class SimulatorResult
    {
        public bool success { get; set; }
        public string message { get; set; }
    }

    public class BitBltCapture : IScreenCapture
    {
        private Rectangle area;

        public bool IsSupported() => true;

        public void Start(Rectangle area)
        {
            this.area = area;
        }

        public void Stop() { }

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h,
            IntPtr hdcSrc, int x1, int y1, int rop);

        const int SRCCOPY = 0x00CC0020;

        public Bitmap GetFrame()
        {
            Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdcDest = g.GetHdc();
                IntPtr hdcSrc = GetDC(IntPtr.Zero);

                BitBlt(hdcDest, 0, 0, area.Width, area.Height,
                    hdcSrc, area.Left, area.Top, SRCCOPY);

                ReleaseDC(IntPtr.Zero, hdcSrc);
                g.ReleaseHdc(hdcDest);
            }

            return bmp;
        }
    }

    public class DxgiCapture : IScreenCapture
    {
        private Device device;
        private OutputDuplication duplication;
        private bool initialized;
        private Rectangle captureArea;

        public bool IsSupported()
        {
            try
            {
                using (var d = new Device(SharpDX.Direct3D.DriverType.Hardware))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public void Start(Rectangle area)
        {
            if (initialized) return;
            captureArea = area;
            captureArea = Rectangle.Intersect(captureArea, Screen.PrimaryScreen.Bounds); // ограничеваем размером экрана

            device = new Device(SharpDX.Direct3D.DriverType.Hardware);

            var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
            var adapter = dxgiDevice.Adapter;
            var output = adapter.Outputs[0];
            var output1 = output.QueryInterface<Output1>();

            duplication = output1.DuplicateOutput(device);

            initialized = true;
        }

        public void Stop()
        {
            try
            {
                duplication?.ReleaseFrame();
            }
            catch
            {
            }

            duplication?.Dispose();
            duplication = null;

            device?.Dispose();
            device = null;

            initialized = false;
        }

        public Bitmap GetFrame()
        {
            SharpDX.DXGI.Resource screenResource = null;

            try
            {
                OutputDuplicateFrameInformation frameInfo;

                duplication.AcquireNextFrame(
                    100,
                    out frameInfo,
                    out screenResource);

                using (var texture = screenResource.QueryInterface<Texture2D>())
                {
                    var desc = texture.Description;

                    // staging texture (CPU readable)
                    var stagingDesc = new Texture2DDescription()
                    {
                        CpuAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None,
                        Format = desc.Format,
                        Width = desc.Width,
                        Height = desc.Height,
                        OptionFlags = ResourceOptionFlags.None,
                        MipLevels = 1,
                        ArraySize = 1,
                        SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging
                    };

                    using (var stagingTex = new Texture2D(device, stagingDesc))
                    {
                        // GPU -> CPU texture copy
                        device.ImmediateContext.CopyResource(texture, stagingTex);

                        // map CPU texture
                        var mapSource = device.ImmediateContext.MapSubresource(
                            stagingTex,
                            0,
                            SharpDX.Direct3D11.MapMode.Read,
                            SharpDX.Direct3D11.MapFlags.None);
                        Bitmap bmp = new Bitmap(
                            captureArea.Width,
                            captureArea.Height,
                            PixelFormat.Format32bppArgb);
                        Rectangle boundsRect = new Rectangle(
                            0,
                            0,
                            captureArea.Width,
                            captureArea.Height);

                        BitmapData mapDest = bmp.LockBits(
                            boundsRect,
                            ImageLockMode.WriteOnly,
                            bmp.PixelFormat);

                        IntPtr sourcePtr = mapSource.DataPointer;
                        IntPtr destPtr = mapDest.Scan0;

                        int sourcePitch = mapSource.RowPitch;
                        int destPitch = mapDest.Stride;

                        for (int y = 0; y < captureArea.Height; y++)
                        {
                            IntPtr src = sourcePtr
                                + (y + captureArea.Top) * sourcePitch
                                + captureArea.Left * 4;

                            IntPtr dst = destPtr
                                + y * destPitch;

                            Utilities.CopyMemory(
                                dst,
                                src,
                                captureArea.Width * 4);
                        }

                        bmp.UnlockBits(mapDest);

                        device.ImmediateContext.UnmapSubresource(stagingTex, 0);

                        return bmp;
                    }
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                screenResource?.Dispose();

                try
                {
                    duplication.ReleaseFrame();
                }
                catch
                {
                }
            }
        }

    }

    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        public static bool BringToFront(IntPtr hWnd)
        {
            // Проверяем что окно существует
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                return false;

            // Если окно свернуто — восстанавливаем
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }

            // Поднимаем окно
            BringWindowToTop(hWnd);

            // Делаем активным
            return SetForegroundWindow(hWnd);
        }

        public static bool WindowExists(IntPtr hWnd)
        {
            return hWnd != IntPtr.Zero && IsWindow(hWnd);
        }
    }

    #endregion
}
