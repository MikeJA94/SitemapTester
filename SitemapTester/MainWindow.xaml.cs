using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Net;
using System.Xml;
using System.Collections.ObjectModel;
using System.Net.Security;
using System.Windows.Input;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Data;
using System.Windows.Media;
using HtmlAgilityPack;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace SitemapTester
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public ObservableCollection<ResultEntry> resultItems = new ObservableCollection<ResultEntry>();
        public ObservableCollection<ResultEntry> resultInternalLinksItems = new ObservableCollection<ResultEntry>();
        public ObservableCollection<ResultEntry> resultItemsPassed = new ObservableCollection<ResultEntry>();

        private bool _checkInternalLinks = false;
        private bool _cancelTesting = false;
        private int _maxRows => 100;


        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;
            
            ResultsDataView.ItemsSource = resultItems;
            ResultsInternalLinksDataView.ItemsSource = resultInternalLinksItems;

        }

        private void CheckBtn_Click(object sender, RoutedEventArgs e)
        {
            CheckSitemapXML(this.SitemapUrl.Text);
        }


        bool CanCheckLink(string url)
        {
            string theUrl = url.ToLower();
            return true;
        }

        bool CanCheckInternalLinks(string url)
        {
            if (!_checkInternalLinks) return false;
            return CanCheckLink(url);
        }


        private void CheckSitemapXML(string siteMapUrl)
        {
            List<string> theUrls = new List<string>();
            int cnt = 1;
            string percent;
            int total = 0;
            int totalErrors = 0;
            
            resultItems.Clear();
            resultInternalLinksItems.Clear();
            ResultsDataViewInternal.ItemsSource = null;
            resultItemsPassed.Clear();

            _cancelTesting = false;
            CheckInternalLinksCheckbox.IsChecked = false;

            SetStatus("Checking Sitemap...");

            XmlDocument doc = new XmlDocument();
            try
            {
                doc.Load(siteMapUrl);
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
                return;
            }

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                string theUrl = node.FirstChild?.InnerText;
                if (CanCheckLink(theUrl))
                    theUrls.Add(theUrl);
            }


            total = theUrls.Count;

            foreach (string theUrl in theUrls)
            {
                if (_cancelTesting)
                    break;
                if (!CanCheckLink(theUrl))
                {
                    cnt++;
                    continue;
                }

                SetStatus($"Checking Sitemap Url {theUrl}...");
                Task<ResultEntry> task = Task.Run<ResultEntry>(async () => await CheckSitemapUrlAsync(theUrl));

                if (task.Result.Status != HttpStatusCode.OK)
                {
                    totalErrors++;
                }
                if (CanCheckInternalLinks(theUrl))
                {
                    task.Result.InternalLinks = GetInternalLinks(task.Result.Html);
                }

                resultItems.Insert(0, task.Result);
                if (task.Result.Status == HttpStatusCode.OK)
                  resultItemsPassed.Insert(0, task.Result);

                percent = Convert.ToDecimal(((float)cnt / (float)total) * 100).ToString("#.##");
                if (percent == string.Empty)
                    percent = "0";

                SetStatusCount($"Errors: {totalErrors} : {cnt}/{total} : {percent}%");
                if (cnt++ > 5000)
                    break;

                if (resultItems.Count > _maxRows)
                {
                    TrimResultList(resultItems);
                }
            }

            
            if (!_cancelTesting)
            {
                StatusText.Text = "Ready";
                MessageBox.Show("Testing Complete !");
            }
            else
                SetStatus($"Testing Canceled...");
            return;
        }

        HttpStatusCode GetHttpStatusCode(System.Exception err)
        {
            if (err is WebException)
            {
                WebException we = (WebException)err;
                if (we.Response is HttpWebResponse)
                {
                    HttpWebResponse response = (HttpWebResponse)we.Response;
                    WebHeaderCollection header = response.Headers;

                    var encoding = ASCIIEncoding.ASCII;
                    using (var reader = new System.IO.StreamReader(response.GetResponseStream(), encoding))
                    {
                        string responseText = reader.ReadToEnd();
                    }
                    return response.StatusCode;
                }
            }
            return HttpStatusCode.ExpectationFailed;
        }


        private async Task<ResultEntry> CheckSitemapUrlAsync(string theUrl)
        {
            return await Task.Run<ResultEntry>(() =>
            {
                ResultEntry result = new ResultEntry();
                HttpStatusCode resultCode = HttpStatusCode.OK;
                RemoteCertificateValidationCallback orgCallback = ServicePointManager.ServerCertificateValidationCallback;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(theUrl);
                    req.Accept = "text/html";
                    HttpWebResponse response = (HttpWebResponse)req.GetResponse();

                    if (CanCheckInternalLinks(theUrl))
                    {
                        var encoding = ASCIIEncoding.ASCII;
                        using (var reader = new System.IO.StreamReader(response.GetResponseStream(), encoding))
                        {
                               result.Html = reader.ReadToEnd();
                        }
                    }
                    response.Close();
                }
                catch (Exception ex)
                {
                    resultCode = GetHttpStatusCode(ex);
                }
                ServicePointManager.ServerCertificateValidationCallback = orgCallback;
                result.Url = theUrl;
                result.Status = resultCode;
                return result;
            });
        }

        List<string> GetInternalLinks(string html)
        {
            // find all links in html 
            try
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);

                var hrefs = doc.DocumentNode.SelectNodes("//a");

                List<string> hrefList = hrefs?.Select(p => p.GetAttributeValue("href", "*")).ToList();
                return hrefList.Where(h => h.ToLower().Contains(".com")).Where(h => !h.Contains("mailto")).Where(h => !h.Contains("www.pinterest")).ToList();
            }

            catch (Exception ex)
            {
                ;
            }
            return new List<string>();
        }


        void TrimResultList(ObservableCollection<ResultEntry> TheList)
        {
            List<ResultEntry> tempList = TheList.ToList();
            foreach (ResultEntry entry in tempList)
            {
                if (entry.Status == HttpStatusCode.OK)
                    TheList.Remove(entry);
            }
        }
        public void SetStatus(string text)
        {
            StatusText.Text = text;
            AllowUIToUpdate();
        }

        public void SetStatusCount(string text)
        {
            StatusCount.Text = text;
            AllowUIToUpdate();
        }

        void AllowUIToUpdate()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, new DispatcherOperationCallback(delegate (object parameter)
            {
                frame.Continue = false;
                return null;
            }), null);
            Dispatcher.PushFrame(frame);

            DispatcherFrame frameInput = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new DispatcherOperationCallback(delegate (object parameter)
            {
                frameInput.Continue = false;
                return null;
            }), null);
            Dispatcher.PushFrame(frameInput);
        }

        private void ResultsDataView_LoadingRow(object sender, System.Windows.Controls.DataGridRowEventArgs e)
        {
            DataGrid theGrid = sender as DataGrid;
            DataGridRow gridRow = e.Row;
            ResultEntry result = gridRow.DataContext as ResultEntry;
            if (result == null) return;
            if (result.Status != HttpStatusCode.OK)
            {
                gridRow.Foreground = new SolidColorBrush(Colors.Red);
            }
            else gridRow.Foreground = new SolidColorBrush(Colors.Black);
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _cancelTesting = true;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // save the Errors to csv file
            var csv = new StringBuilder();
            csv.AppendLine(string.Format("{0},{1}", "Url", "Status"));

            foreach (ResultEntry result in resultItems)
            {
                if (result.Status != HttpStatusCode.OK)
                {
                    var newLine = string.Format("{0},{1}", result.Url, result.Status);
                    csv.AppendLine(newLine);
                }
            }
            File.WriteAllText("BrokenSitemapLinks.csv", csv.ToString());

            // now save the internal links if enabled
            if (_checkInternalLinks)
            {
                csv.Clear();
                csv.AppendLine(string.Format("{0},{1},{2}", "BaseUrl", "Url", "Status"));
                foreach (ResultEntry result in resultInternalLinksItems)
                {
                    if (result.Status != HttpStatusCode.OK)
                    {
                        var newLine = string.Format("{0},{1},{2}", result.BaseUrl, result.Url, result.Status);
                        csv.AppendLine(newLine);
                    }
                }
                File.WriteAllText("BrokenContentLinks.csv", csv.ToString());
            }

            SetStatus("Data Saved..");
        }

        private void CheckInternalLinksCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            // fill up list of links to check the internal links 
            if (CheckInternalLinksCheckbox.IsChecked == true)
            {
                ResultsDataViewInternal.ItemsSource = resultItemsPassed;
                _checkInternalLinks = true;
            }
            else
            {
                resultItemsPassed.Clear();
                _checkInternalLinks = false;
            }
            CheckInternalLinksBtn.IsEnabled = (CheckInternalLinksCheckbox.IsChecked == true);
            _cancelTesting = false;

        }

        private void CheckInternalLinksBtn_Click(object sender, RoutedEventArgs e)
        {
            int cnt = 1;
            string percent;
            int total = 0;
            int totalErrors = 0;

            resultInternalLinksItems.Clear();

            foreach (ResultEntry entry in resultItemsPassed)
            {
                if (!entry.Checked)
                    continue;
                if (_cancelTesting)
                    break;

                SetStatus($"Checking Sitemap Url {entry.Url}...");
                Task<ResultEntry> task = Task.Run<ResultEntry>(async () => await CheckSitemapUrlAsync(entry.Url));
                
                if (CanCheckInternalLinks(entry.Url))
                {
                    task.Result.InternalLinks = GetInternalLinks(task.Result.Html);
                    total = task.Result.InternalLinks.Count;
                    cnt = 1;
                    foreach (string url in task.Result.InternalLinks)
                    {
                        if (_cancelTesting)
                            break;
                        SetStatus($"Checking Internal Link  {url}...");
                        Task<ResultEntry> taskLink = Task.Run<ResultEntry>(async () => await CheckSitemapUrlAsync(url));
                           if (taskLink.Result.Status != HttpStatusCode.OK)
                           {
                                taskLink.Result.BaseUrl = entry.Url;
                                resultInternalLinksItems.Insert(0, taskLink.Result);
                                totalErrors++;
                           }
                           if (resultInternalLinksItems.Count > _maxRows)
                           {
                              TrimResultList(resultInternalLinksItems);
                           }
                        percent = Convert.ToDecimal(((float)cnt / (float)total) * 100).ToString("#.##");
                        if (percent == string.Empty)
                            percent = "0";
                        SetStatusCount($"Errors: {totalErrors} : {cnt}/{total} : {percent}%");
                        cnt++;
                     }
                }
            }
            if (!_cancelTesting)
                StatusText.Text = "Ready";
            else
                SetStatus($"Testing Canceled...");
        }

        private void ToggleCheckBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (ResultEntry entry in resultItemsPassed)
            {
                entry.Checked = !entry.Checked;
            }
            ResultsDataViewInternal.ItemsSource = resultItemsPassed;
        }
    }

    public class ResultEntry
    {

        public bool Checked { get; set; }
        public string Url { get; set; }
        public HttpStatusCode Status {  get ; set; }
        public List<string> InternalLinks { get; set; }

        public string BaseUrl { get; set; }

        public string Html { get; set; }

        public ResultEntry()
        {
            InternalLinks = new List<string>();
        }
    }
}
