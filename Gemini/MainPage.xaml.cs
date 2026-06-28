using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using System.Text.RegularExpressions;
using Windows.UI.Text;
using System.Linq;

namespace Gemini
{
    public class ChatSessionItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Time { get; set; }
    }

    public sealed partial class MainPage : Page
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private string ModelName = "gemini-2.5-flash";
        private const string HistoryFileName = "chats_storage.json";
        private string currentSessionId = string.Empty;
        private readonly Windows.ApplicationModel.Resources.ResourceLoader resourceLoader = Windows.ApplicationModel.Resources.ResourceLoader.GetForCurrentView();


        public MainPage()
        {
            this.InitializeComponent();
            ApiKeyInput.Password = LoadApiKey();
            LoadThemePreference();
            LoadModelPreference();
            LoadLanguagePreference();
            StartNewSession();

            // Сразу загружаем существующую историю в боковое меню
            var t = RefreshHistoryListAsync();

            UpdateMenuVisibility();
        }

        private void UpdateMenuVisibility()
        {
            if (RootSplitView == null) return;

            var visibility = RootSplitView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;

            if (HistoryHeader != null) HistoryHeader.Visibility = visibility;
            if (HistoryListView != null) HistoryListView.Visibility = visibility;
            if (ChatMenuButtonText != null) ChatMenuButtonText.Visibility = visibility;
            if (SettingsMenuButtonText != null) SettingsMenuButtonText.Visibility = visibility;
        }

        private void LoadLanguagePreference()
        {
            string currentLang = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
            if (string.IsNullOrEmpty(currentLang))
            {
                currentLang = Windows.Globalization.ApplicationLanguages.Languages[0];
            }

            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag.ToString() == currentLang)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox == null || LanguageComboBox.SelectedItem == null) return;

            var selectedItem = LanguageComboBox.SelectedItem as ComboBoxItem;
            string newLang = selectedItem.Tag.ToString();

            if (Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride != newLang)
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = newLang;
                // Refresh the page to apply changes
                Frame.Navigate(this.GetType());
            }
        }

        private void LoadModelPreference()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey("SelectedModel"))
            {
                ModelName = localSettings.Values["SelectedModel"].ToString();
            }

            foreach (ComboBoxItem item in ModelComboBox.Items)
            {
                if (item.Content.ToString() == ModelName)
                {
                    ModelComboBox.SelectedItem = item;
                    break;
                }
            }

            if (ModelComboBox.SelectedItem == null) ModelComboBox.SelectedIndex = 0;
        }

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelComboBox == null || ModelComboBox.SelectedItem == null) return;

            var selectedItem = ModelComboBox.SelectedItem as ComboBoxItem;
            ModelName = selectedItem.Content.ToString();

            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["SelectedModel"] = ModelName;
        }

        private void LoadThemePreference()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey("AppTheme"))
            {
                int theme = (int)localSettings.Values["AppTheme"];
                ThemeComboBox.SelectedIndex = theme;
                ApplyTheme((ElementTheme)theme);
            }
            else
            {
                ThemeComboBox.SelectedIndex = 0; // System default
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox == null) return;

            int selectedIndex = ThemeComboBox.SelectedIndex;
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["AppTheme"] = selectedIndex;

            ApplyTheme((ElementTheme)selectedIndex);
        }

        private void ApplyTheme(ElementTheme theme)
        {
            FrameworkElement rootElement = Window.Current.Content as FrameworkElement;
            if (rootElement != null)
            {
                rootElement.RequestedTheme = theme;
            }
        }

        private void StartNewSession()
        {
            currentSessionId = Guid.NewGuid().ToString();

            ChatLog.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run { Text = resourceLoader.GetString("NewSessionWelcome") });
            ChatLog.Blocks.Add(paragraph);
        }

        private void NewChatButton_Click(object sender, RoutedEventArgs e)
        {
            StartNewSession();
            ChatMenuButton.IsChecked = true;
            SwitchToPane("chat");
        }

        private async void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;
            UpdateMenuVisibility();
            if (RootSplitView.IsPaneOpen)
            {
                await RefreshHistoryListAsync();
            }
        }

        private void MenuButton_Checked(object sender, RoutedEventArgs e)
        {
            if (ChatPane == null || SettingsPane == null) return;

            RadioButton rb = sender as RadioButton;
            if (rb == null) return;

            StatusMessage.Visibility = Visibility.Collapsed;
            HistoryStatusMessage.Visibility = Visibility.Collapsed;

            if (rb == ChatMenuButton)
            {
                SwitchToPane("chat");
            }
            else if (rb == SettingsMenuButton)
            {
                SwitchToPane("settings");
            }
        }

        private void SwitchToPane(string pane)
        {
            ChatPane.Visibility = (pane == "chat") ? Visibility.Visible : Visibility.Collapsed;
            SettingsPane.Visibility = (pane == "settings") ? Visibility.Visible : Visibility.Collapsed;

            if (pane == "chat") TitleText.Text = resourceLoader.GetString("ChatTitle");
            if (pane == "settings") TitleText.Text = resourceLoader.GetString("SettingsTitle");
        }

        #region Обработка API
        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = LoadApiKey();
            string prompt = UserInput.Text.Trim();

            if (string.IsNullOrEmpty(apiKey))
            {
                AppendSystemMessage(resourceLoader.GetString("ApiKeyWarning"));
                return;
            }

            if (string.IsNullOrEmpty(prompt)) return;

            // Упрощённая очистка приветственного сообщения
            if (ChatLog.Blocks.Count > 0)
            {
                var firstBlock = ChatLog.Blocks[0] as Paragraph;
                if (firstBlock != null && firstBlock.Inlines.Count > 0)
                {
                    var firstRun = firstBlock.Inlines[0] as Run;
                    if (firstRun != null && firstRun.Text != null && (firstRun.Text.Contains("Открыт новый диалог") || firstRun.Text.Contains("New dialog opened")))
                    {
                        ChatLog.Blocks.Clear();
                    }
                }
            }

            UserInput.Text = string.Empty;
            AppendToChat(resourceLoader.GetString("YouLabel"), prompt);
            await SaveMessageToHistoryAsync(currentSessionId, resourceLoader.GetString("YouLabel"), prompt);

            try
            {
                string url = $"https://generativelanguage.googleapis.com/v1/models/{ModelName}:generateContent?key={apiKey}";

                JsonObject rootObj = new JsonObject();
                JsonArray contentsArray = new JsonArray();
                JsonObject contentObj = new JsonObject();
                JsonArray partsArray = new JsonArray();
                JsonObject textObj = new JsonObject();

                textObj.Add("text", JsonValue.CreateStringValue(prompt));
                partsArray.Add(textObj);
                contentObj.Add("parts", partsArray);
                contentsArray.Add(contentObj);
                rootObj.Add("contents", contentsArray);

                var httpContent = new StringContent(rootObj.Stringify(), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.PostAsync(url, httpContent);
                string responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    JsonObject jsonResponse = JsonObject.Parse(responseString);
                    JsonArray candidates = jsonResponse.GetNamedArray("candidates");

                    JsonObject firstCandidate = candidates[0].GetObject();
                    JsonObject contentRes = firstCandidate.GetNamedObject("content");
                    JsonArray partsRes = contentRes.GetNamedArray("parts");
                    string reply = partsRes[0].GetObject().GetNamedString("text");

                    AppendToChat("Gemini", reply);
                    await SaveMessageToHistoryAsync(currentSessionId, "Gemini", reply);

                    await RefreshHistoryListAsync();
                }
                else
                {
                    AppendSystemMessage($"Ошибка API ({response.StatusCode}): {responseString}");
                }
            }
            catch (Exception ex)
            {
                AppendSystemMessage($"Ошибка приложения: {ex.Message}");
            }
        }

        private Paragraph ParseInlineMarkdown(string text)
        {
            var paragraph = new Paragraph();
            if (string.IsNullOrEmpty(text)) return paragraph;

            int i = 0;
            while (i < text.Length)
            {
                // 1. Жирный **текст**
                if (i + 3 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2);
                    if (end != -1)
                    {
                        string innerText = text.Substring(i + 2, end - i - 2);
                        if (!string.IsNullOrEmpty(innerText))
                        {
                            paragraph.Inlines.Add(new Run
                            {
                                Text = innerText,
                                FontWeight = FontWeights.Bold
                            });
                            i = end + 2;
                            continue;
                        }
                    }
                }

                // 2. Курсив *текст*
                if (text[i] == '*')
                {
                    int end = text.IndexOf('*', i + 1);
                    if (end != -1 && end > i + 1)
                    {
                        // Проверяем, что это не ** (который мы уже обработали бы выше, если бы он был закрыт)
                        // Но если это просто один *, ищем следующий *
                        string innerText = text.Substring(i + 1, end - i - 1);
                        if (!string.IsNullOrEmpty(innerText))
                        {
                            paragraph.Inlines.Add(new Run
                            {
                                Text = innerText,
                                FontStyle = Windows.UI.Text.FontStyle.Italic
                            });
                            i = end + 1;
                            continue;
                        }
                    }
                }

                // 3. Код `код`
                if (text[i] == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end != -1)
                    {
                        string innerText = text.Substring(i + 1, end - i - 1);
                        paragraph.Inlines.Add(new Run
                        {
                            Text = innerText,
                            FontFamily = new FontFamily("Consolas"),
                            Foreground = new SolidColorBrush(Windows.UI.Colors.DarkBlue)
                        });
                        i = end + 1;
                        continue;
                    }
                }

                // Если ничего не подошло, ищем следующий возможный тег
                int nextStart = text.IndexOfAny(new[] { '*', '`' }, i + 1);
                if (nextStart == -1)
                {
                    paragraph.Inlines.Add(new Run { Text = text.Substring(i) });
                    break;
                }
                else
                {
                    paragraph.Inlines.Add(new Run { Text = text.Substring(i, nextStart - i) });
                    i = nextStart;
                }
            }

            return paragraph;
        }

        private IEnumerable<Block> ParseMarkdown(string markdown)
        {
            var blocks = new List<Block>();
            if (string.IsNullOrEmpty(markdown)) return blocks;

            // Нормализация переносов строк
            string normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split('\n');

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                // Пустая строка
                if (string.IsNullOrEmpty(trimmedLine))
                {
                    blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 5) });
                    continue;
                }

                // Заголовки
                if (trimmedLine.StartsWith("# "))
                {
                    var p = ParseInlineMarkdown(trimmedLine.Substring(2));
                    foreach (var inline in p.Inlines)
                    {
                        var run = inline as Run;
                        if (run != null)
                        {
                            run.FontSize = 22;
                            run.FontWeight = FontWeights.Bold;
                        }
                    }
                    blocks.Add(p);
                    continue;
                }
                if (trimmedLine.StartsWith("## "))
                {
                    var p = ParseInlineMarkdown(trimmedLine.Substring(3));
                    foreach (var inline in p.Inlines)
                    {
                        var run = inline as Run;
                        if (run != null)
                        {
                            run.FontSize = 19;
                            run.FontWeight = FontWeights.Bold;
                        }
                    }
                    blocks.Add(p);
                    continue;
                }

                // Списки
                if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
                {
                    var p = ParseInlineMarkdown(trimmedLine.Substring(2));
                    p.Inlines.Insert(0, new Run { Text = " • " });
                    blocks.Add(p);
                    continue;
                }

                // Нумерованные списки
                Match numbered = Regex.Match(trimmedLine, @"^(\d+)\.\s+(.*)$");
                if (numbered.Success)
                {
                    string num = numbered.Groups[1].Value;
                    string content = numbered.Groups[2].Value;
                    var p = ParseInlineMarkdown(num + ". " + content);
                    blocks.Add(p);
                    continue;
                }

                // Обычный параграф
                blocks.Add(ParseInlineMarkdown(line));
            }

            return blocks;
        }

        private void AppendToChat(string sender, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            // Заголовок сообщения (отправитель)
            var header = new Paragraph { Margin = new Thickness(0, 10, 0, 2) };
            header.Inlines.Add(new Run { Text = sender + ": ", FontWeight = FontWeights.Bold });
            ChatLog.Blocks.Add(header);

            // Тело сообщения (Markdown)
            foreach (var block in ParseMarkdown(message))
            {
                ChatLog.Blocks.Add(block);
            }

            // Прокрутка
            ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null);
        }

        private void AppendSystemMessage(string message)
        {
            var p = new Paragraph { Margin = new Thickness(0, 5, 0, 5) };
            p.Inlines.Add(new Run { Text = resourceLoader.GetString("SystemLabel") + ": " + message, FontStyle = Windows.UI.Text.FontStyle.Italic, Foreground = new SolidColorBrush(Windows.UI.Colors.Gray) });
            ChatLog.Blocks.Add(p);
        }

        private void UserInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                SendButton_Click(this, new RoutedEventArgs());
            }
        }
        #endregion

        #region Клик по элементу истории в Гамбургер-меню
        private async void HistoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = HistoryListView.SelectedItem as ChatSessionItem;
            if (selectedItem == null) return;

            // Очищаем и загружаем сессию
            ChatLog.Blocks.Clear();
            currentSessionId = selectedItem.Id;

            try
            {
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await localFolder.GetFileAsync(HistoryFileName);
                string content = await FileIO.ReadTextAsync(file);

                JsonArray rootArray = JsonArray.Parse(content);
                foreach (var item in rootArray)
                {
                    var obj = item.GetObject();
                    if (obj.GetNamedString("id") == selectedItem.Id)
                    {
                        JsonArray messages = obj.GetNamedArray("messages");
                        foreach (var msgToken in messages)
                        {
                            var msgObj = msgToken.GetObject();
                            string msgSender = msgObj.GetNamedString("sender");
                            string msgText = msgObj.GetNamedString("text");

                            AppendToChat(msgSender, msgText);
                        }
                        break;
                    }
                }
            }
            catch { AppendSystemMessage(resourceLoader.GetString("SessionRestoreError")); }

            ChatMenuButton.IsChecked = true;
            SwitchToPane("chat");
            RootSplitView.IsPaneOpen = false;
            UpdateMenuVisibility();
            HistoryListView.SelectedItem = null;
        }
        #endregion

        #region Файловое хранилище JSON
        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["ApiKey"] = ApiKeyInput.Password.Trim();
            StatusMessage.Visibility = Visibility.Visible;
        }

        private string LoadApiKey()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            return localSettings.Values["ApiKey"]?.ToString() ?? string.Empty;
        }

        private async Task SaveMessageToHistoryAsync(string sessionId, string sender, string text)
        {
            try
            {
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await localFolder.CreateFileAsync(HistoryFileName, CreationCollisionOption.OpenIfExists);
                string currentContent = await FileIO.ReadTextAsync(file);

                JsonArray rootArray = string.IsNullOrEmpty(currentContent) ? new JsonArray() : JsonArray.Parse(currentContent);
                JsonObject targetSession = null;

                foreach (var item in rootArray)
                {
                    var obj = item.GetObject();
                    if (obj.GetNamedString("id") == sessionId)
                    {
                        targetSession = obj;
                        break;
                    }
                }

                if (targetSession == null)
                {
                    targetSession = new JsonObject();
                    targetSession.Add("id", JsonValue.CreateStringValue(sessionId));
                    string shortTitle = text.Length > 30 ? text.Substring(0, 27) + "..." : text;
                    targetSession.Add("title", JsonValue.CreateStringValue(shortTitle));
                    targetSession.Add("time", JsonValue.CreateStringValue(DateTime.Now.ToString("dd.MM HH:mm")));
                    targetSession.Add("messages", new JsonArray());
                    rootArray.Add(targetSession);
                }

                JsonArray messagesArray = targetSession.GetNamedArray("messages");
                JsonObject newMessage = new JsonObject();
                newMessage.Add("sender", JsonValue.CreateStringValue(sender));
                newMessage.Add("text", JsonValue.CreateStringValue(text));
                messagesArray.Add(newMessage);

                await FileIO.WriteTextAsync(file, rootArray.Stringify());
            }
            catch { }
        }

        private async Task RefreshHistoryListAsync()
        {
            List<ChatSessionItem> itemsList = new List<ChatSessionItem>();
            try
            {
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await localFolder.GetFileAsync(HistoryFileName);
                string content = await FileIO.ReadTextAsync(file);

                if (!string.IsNullOrEmpty(content))
                {
                    JsonArray rootArray = JsonArray.Parse(content);
                    for (int i = (int)rootArray.Count - 1; i >= 0; i--)
                    {
                        var obj = rootArray[i].GetObject();
                        itemsList.Add(new ChatSessionItem
                        {
                            Id = obj.GetNamedString("id"),
                            Title = obj.GetNamedString("title"),
                            Time = obj.GetNamedString("time")
                        });
                    }
                }
            }
            catch { }

            HistoryListView.ItemsSource = itemsList;
        }

        private async Task<string> GetSessionChatLogAsync(string sessionId)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await localFolder.GetFileAsync(HistoryFileName);
                string content = await FileIO.ReadTextAsync(file);

                JsonArray rootArray = JsonArray.Parse(content);
                foreach (var item in rootArray)
                {
                    var obj = item.GetObject();
                    if (obj.GetNamedString("id") == sessionId)
                    {
                        JsonArray messages = obj.GetNamedArray("messages");
                        foreach (var msgToken in messages)
                        {
                            var msgObj = msgToken.GetObject();
                            sb.AppendLine($"{msgObj.GetNamedString("sender")}: {msgObj.GetNamedString("text")}\n");
                        }
                        break;
                    }
                }
            }
            catch { return resourceLoader.GetString("SessionRestoreError"); }

            return sb.ToString().TrimEnd();
        }

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await localFolder.GetFileAsync(HistoryFileName);
                await file.DeleteAsync();

                HistoryStatusMessage.Visibility = Visibility.Visible;
                HistoryListView.ItemsSource = null;
                StartNewSession();
            }
            catch { HistoryStatusMessage.Visibility = Visibility.Visible; }
        }
        #endregion

        #region Обработка информационной панели
        private void OpenInfoPanel_Click(object sender, RoutedEventArgs e)
        {
            InfoOverlay.Visibility = Visibility.Visible;
            SlideInAnimation.Begin();
        }

        private void CloseInfoPanel_Click(object sender, RoutedEventArgs e)
        {
            SlideOutAnimation.Completed += (s, ev) =>
            {
                InfoOverlay.Visibility = Visibility.Collapsed;
            };
            SlideOutAnimation.Begin();
        }

        private void InfoPanel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Предотвращаем закрытие при клике по самой панели (т.к. Overlay ловит клик)
            e.Handled = true;
        }
        #endregion
    }
}