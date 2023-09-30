using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RFBRDownloader
{
    public partial class MainWindow : Window
    {
        const string URI_READER_PREFIX = "https://www.rfbr.ru/rffi/ru/books/o_";
        const string URI_PAGE_PREFIX = "https://www.rfbr.ru/rffi/djvu_page?objectId=";
        const string BOOK_NAME = "Книга с РФФИ.pdf";

        public MainWindow()
        {
            InitializeComponent();
            StateInitial();
        }

        #region States

        private void StateInitial()
        {
            TextLoadStatus.Text = "Вставьте ссылку на книгу и нажмите \"Скачать\"";
            Progress.Visibility = Visibility.Collapsed;
            ButtonDownload.IsEnabled = true;
        }

        private void StatePreparing()
        {
            TextLoadStatus.Text = "Подготовка к загрузке книги...";

            Progress.IsIndeterminate = true;
            Progress.Visibility = Visibility.Visible;

            ButtonDownload.IsEnabled = false;
        }

        private void StateLoading(int currentPage, int total)
        {
            TextLoadStatus.Text = $"Загрузка страницы {currentPage + 1} из {total}";

            Progress.IsIndeterminate = false;
            Progress.Minimum = 0;
            Progress.Maximum = total;
            Progress.Value = currentPage;
            Progress.Visibility = Visibility.Visible;

            ButtonDownload.IsEnabled = false;
        }

        private void StateBuilding(int currentPage, int total)
        {
            TextLoadStatus.Text = $"Сборка документа. Страница {currentPage + 1} из {total}";

            Progress.IsIndeterminate = false;
            Progress.Minimum = 0;
            Progress.Maximum = total;
            Progress.Value = currentPage;
            Progress.Visibility = Visibility.Visible;

            ButtonDownload.IsEnabled = false;
        }

        private void StateError(string error)
        {
            TextLoadStatus.Text = $"Ошибка загрузки книги: {error}";
            Progress.Visibility = Visibility.Collapsed;
            Progress.IsIndeterminate = false;
            ButtonDownload.IsEnabled = true;
        }

        #endregion

        private async void ButtonDownload_Click(object sender, RoutedEventArgs e)
        {
            string id;
            if (!TryToGetBookId(out id))
            {
                ShowError("Ошибка! Введён некорректный url адрес. Пример: rfbr.ru/rffi/ru/books/o_36464");
                return;
            }

            StatePreparing();

            try
            {
                await DownloadBook(id);

                ShowInfo("Загрузка завершена!");
                StateInitial();
            }
            catch (Exception ex)
            {
                ShowError(ex.ToString());
                StateError($"{ex.Message}");
            }
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

        private async Task DownloadBook(string id)
        {
            var pagesCount = await GetPagesCount(id);

            string tempDirPath = GetTemporaryDirectory();

            var progress = 0;
            await DownloadBookPages(id, pagesCount, tempDirPath, () => {
                Interlocked.Increment(ref progress);
                if (progress != pagesCount) StateLoading(progress, pagesCount);
            });

            var buildingProgress = new Progress<int>((i) => StateBuilding(i, pagesCount));
            await Task.Run(() =>
            {
                BuildBook(id, pagesCount, tempDirPath, buildingProgress);
            });
        }

        private async Task<int> GetPagesCount(string bookId)
        {
            var client = new HttpClient();
            var response = await client.GetAsync($"{URI_READER_PREFIX}{bookId}#1");
            var document = await response.Content.ReadAsStringAsync();
            var regex = new Regex(@"readerInitialization\((\d+)");
            var match = regex.Match(document);

            if (match.Groups.Count == 2)
            {
                var pagesCount = match.Groups[1].ToString();
                return int.Parse(pagesCount);
            }
            else
            {
                throw new DocumentException("Не удалось понять количество страниц в книге. Возможно, сайт был обновлён и всё сломалось :(");
            }
        }

        private async Task DownloadBookPages(string id, int pagesCount, string dir, Action step)
        {
            var dop = 8;
            var semaphore = new SemaphoreSlim(initialCount: dop, maxCount: dop);

            var tasks = Enumerable.Range(0, pagesCount).Select(async i =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var pageUrl = new Uri($"{URI_PAGE_PREFIX}{id}&page={i}");
                    var pagePath = Path.Combine(dir, $"{i}.png");

                    await DownloadFile(pageUrl, pagePath);

                    step();
                }
                catch
                {
                    ShowError("Поц!");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task DownloadFile(Uri uri, string path)
        {
            var client = new HttpClient();
            var fs = new FileStream(path, FileMode.OpenOrCreate);

            var s = await client.GetStreamAsync(uri);
            await s.CopyToAsync(fs);

            await fs.FlushAsync();
            fs.Close();
        }

        private void BuildBook(string id, int pagesCount, string dir, Progress<int> progress)
        {
            Document document = new Document();
            using (var stream = new FileStream(BOOK_NAME, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                PdfWriter.GetInstance(document, stream);
                document.Open();

                for (int i = 0; i < pagesCount; i++)
                {
                    // TODO: Move path generation to the function.
                    var pagePath = Path.Combine(dir, $"{i}.png");
                    var convertedPagePath = Path.Combine(dir, $"{i}.jpg");

                    // Sometimes, RFBR.ru returns zero-length pages.
                    if (new FileInfo(pagePath).Length == 0)
                    {
                        continue;
                    }

                    // Error handling in System.Image is very bad.
                    // Out-of-Memory exception causes in many cases.
                    try
                    {
                        ConvertPngToJpg(pagePath, convertedPagePath);
                    }
                    catch (OutOfMemoryException)
                    {
                        throw new Exception($"Failed to convert file {pagePath} to JPG");
                    }

                    AppendJpgToDocument(document, convertedPagePath);

                    ((IProgress<int>)progress).Report(i);
                }

                document.Close();
            }
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
                
                pageImage.ScalePercent((float)(800.0 / pageImage.Height * 100.0));

                document.Add(pageImage);
                document.NewPage();
            }
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

    }
}
