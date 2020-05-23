using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Image = iTextSharp.text.Image;

namespace rffi_loader
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            TextLoadStatus.Text = "Введите данные";
            VisualProgress.Visibility = Visibility.Collapsed;

        }

        public string GetTemporaryDirectory()
        {
            string tempDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
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

        private async void ButtonDownload_Click(object sender, RoutedEventArgs e)
        {
            int pagesCount;
            string linkPrefix = "https://www.rfbr.ru/rffi/djvu_page?objectId=";

            Uri uri;
            string rawUri = InputUrl.Text;

            try
            {
                linkPrefix += new Regex(@"books\/o_(\d+)").Match(rawUri).Groups[1];
            }
            catch
            {
                ShowError(@"Ошибка! Введён некорректный url адрес. Пример: https://www.rfbr.ru/rffi/ru/books/o_36464#1");
                return;
            }

            if (!int.TryParse(InputPagesCount.Text, out pagesCount))
            {
                ShowError("Ошибка! Введите число страниц");
                return;
            }

            TextLoadStatus.Text = "Загрузка...";
            VisualProgress.Visibility = Visibility.Visible;
            ButtonDownload.IsEnabled = false;

            await Task.Run(() =>
            {
                var temp = GetTemporaryDirectory();

                Document document = new Document();
                using (var stream = new FileStream("Book.pdf", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    PdfWriter.GetInstance(document, stream);
                    document.Open();

                    using (var client = new WebClient())
                    {
                        for (int i = 0; i < pagesCount; i++)
                        {
                            var pathToImg = temp + $"\\{i + 1}.png";

                            client.DownloadFile(linkPrefix + "&page=" + i, pathToImg);
                            Console.WriteLine($"Download page {i + 1} to {pathToImg}");

                            System.Drawing.Image img = System.Drawing.Image.FromFile(pathToImg);
                            img.Save(pathToImg + ".jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
                            Console.WriteLine($"Page {i + 1} converted to jpg");

                            using (var imageStream = new FileStream(pathToImg + ".jpg", FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                var pic = Image.GetInstance(imageStream);
                                if (pic.Height > pic.Width)
                                {
                                    //Maximum height is 800 pixels.
                                    float percentage = 0.0f;
                                    percentage = 800 / pic.Height;
                                    pic.ScalePercent(percentage * 100);
                                }
                                else
                                {
                                    //Maximum width is 600 pixels.
                                    float percentage = 0.0f;
                                    percentage = 600 / pic.Width;
                                    pic.ScalePercent(percentage * 100);
                                }

                                document.Add(pic);
                                document.NewPage();
                            }
                            Console.WriteLine($"Append page {i + 1} into pdf");
                        }
                    }

                    document.Close();
                }
            });

            TextLoadStatus.Text = "Введите данные выше";
            VisualProgress.Visibility = Visibility.Collapsed;
            ButtonDownload.IsEnabled = true;

            MessageBox.Show(
                    "Загрузка завершена!",
                    "",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
        }
    }
}
