using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace RFBRDownloader
{
    public partial class MainWindow : Window
    {
        const string URI_PAGE_LINK_PREFIX = "https://www.rfbr.ru/rffi/djvu_page?objectId=";

        public MainWindow()
        {
            InitializeComponent();
            StateInitial();
        }

        #region States

        private void StateInitial()
        {
            TextLoadStatus.Text = "Введите данные и нажмите \"Скачать\"";
            Progress.Visibility = Visibility.Collapsed;
            ButtonDownload.IsEnabled = true;
        }

        private void StateLoading()
        {
            TextLoadStatus.Text = "Загрузка...";
            Progress.Visibility = Visibility.Visible;
            ButtonDownload.IsEnabled = false;
        }

        private void InitializeProgress(int max)
        {

            Progress.IsIndeterminate = false;
            Progress.Maximum = max;
            Progress.Minimum = 0;
        }

        #endregion

        private async void ButtonDownload_Click(object sender, RoutedEventArgs e)
        {
            string bookId;
            int pagesCount;

            if (!TryToGetBookId(out bookId))
            {
                ShowError("Ошибка! Введён некорректный url адрес. Пример: rfbr.ru/rffi/ru/books/o_36464");
                return;
            }

            if (!TryToGetPagesCount(out pagesCount))
            {
                ShowError("Ошибка! Введите число страниц");
                return;
            }

            StateLoading();
            InitializeProgress(pagesCount);

            var downloadProgress = new Progress<int>((value) =>
            {
                TextLoadStatus.Text = $"Загружено {value} из {pagesCount}";
                Progress.Value = value;
            });

            await Task.Run(() =>
            {
                string pageUrl;
                string pagePngPath;
                string pageJpgPath;
                string tempDirPath = GetTemporaryDirectory();

                Document document = new Document();
                using (var stream = new FileStream("Book.pdf", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    PdfWriter.GetInstance(document, stream);
                    document.Open();

                    var client = new WebClient();

                    for (int i = 0; i < pagesCount; i++)
                    {
                        pageUrl = $"{URI_PAGE_LINK_PREFIX}{bookId}&page={i}";
                        pagePngPath = Path.Combine(tempDirPath, $"{i + 1}.png");
                        pageJpgPath = Path.Combine(tempDirPath, $"{i + 1}.jpg");

                        client.DownloadFile(pageUrl, pagePngPath);

                        ((IProgress<int>)downloadProgress).Report(i + 1);

                        ConvertPngToJpg(pagePngPath, pageJpgPath);
                        AppendJpgToDocument(document, pageJpgPath);
                    }

                    client.Dispose();
                    document.Close();
                }
            });

            ShowInfo("Загрузка завершена!");
            StateInitial();
        }

        private bool TryToGetBookId(out string bookId)
        {
            var regex = new Regex(@"books\/o_(\d+)");
            var match = regex.Match(InputUrl.Text);

            if (match.Groups.Count != 2)
            {
                bookId = "";
                return false;
            }
            else
            {
                bookId = match.Groups[1].ToString();
                return true;
            }
        }

        private bool TryToGetPagesCount(out int pagesCount)
        {
            return int.TryParse(InputPagesCount.Text, out pagesCount);
        }

        private void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        private void ShowInfo(string message)
        {
            MessageBox.Show(
                message,
                "",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        private void ConvertPngToJpg(string pngPath, string jpgPath)
        {
            System.Drawing.Image img = System.Drawing.Image.FromFile(pngPath);
            img.Save(jpgPath, System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        private void AppendJpgToDocument(Document document, string jpgPath)
        {
            using (var imageStream = new FileStream(jpgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var pageImage = Image.GetInstance(imageStream);

                pageImage.ScalePercent(800 / pageImage.Height * 100);

                document.Add(pageImage);
                document.NewPage();
            }
        }
    }
}
