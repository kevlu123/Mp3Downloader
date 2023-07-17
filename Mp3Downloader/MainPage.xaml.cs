using ScraperCollection.Bing;
using ScraperCollection.BestMp3Converter;

namespace Mp3Downloader;

static class Extensions {
	public static void ContinueWithOnMainThread(this Task task, Action<Task> action) {
		task.ContinueWith(t => MainThread.BeginInvokeOnMainThread(() => action(t)));
	}

	public static void ContinueWithOnMainThread<T>(this Task<T> task, Action<Task<T>> action) {
		task.ContinueWith(t => MainThread.BeginInvokeOnMainThread(() => action(t)));
	}
}

public partial class MainPage : ContentPage {
	public static MainPage Instance { get; private set; }

	private CancellationTokenSource lastCancellationTokenSource;

	public MainPage() {
		Instance = this;
		InitializeComponent();

#if ANDROID
		MainActivity.Instance.RequestStoragePermission();
#endif
	}

	private void SearchButton_Clicked(object sender, EventArgs e) {
		DownloadsPanel.Children.Clear();

		var query = InputBox.Text.Trim();
		if (query.Length == 0) {
			return;
		}

		static bool IsYoutubeUrl(string url) => url.StartsWith("https://www.youtube.com/watch?v=");

		lastCancellationTokenSource?.Cancel();
		var cts = new CancellationTokenSource();
		lastCancellationTokenSource = cts;

		if (IsYoutubeUrl(query)) {
			BestMp3ConverterScraper.GetMp3Options(query, cts.Token)
				.ContinueWithOnMainThread(task => {
					try {
						if (task.IsCanceled) {
							return;
						} else if (task.IsFaulted) {
							ShowError(task.Exception.InnerException.ToString());
							return;
						} else if (task.Result.Count == 0) {
							ShowError("No downloads available.");
							return;
						}

						var options = task.Result;
						var best = options.MaxBy(opt => opt.Kbps);
						AddDownloadOption(best, 0);
					} finally {
						DisposeCancellationTokenSource(cts);
					}
                });
		} else {
			var task = new Func<Task>(async () => {
				var searchResults = await BingScraper.Search(query, cts.Token);
				var youtubeUrls = searchResults
                    .Select(entry => entry.Url)
                    .Where(IsYoutubeUrl)
					.ToList();

				var completedCount = 0;
				var failedCount = 0;
				for (int i = 0; i < youtubeUrls.Count; i++) {
					var url = youtubeUrls[i];
					var priority = i;
					BestMp3ConverterScraper.GetMp3Options(url, cts.Token)
						.ContinueWithOnMainThread(subtask => {
                            if (subtask.IsCompletedSuccessfully && subtask.Result.Count > 0) {
                            	var best = subtask.Result.MaxBy(opt => opt.Kbps);
                            	AddDownloadOption(best, priority);
                            } else {
                                Interlocked.Increment(ref failedCount);
                            }
                            Interlocked.Increment(ref completedCount);
                        });
				}

				do {
					await Task.Delay(500, CancellationToken.None);
				} while (completedCount < youtubeUrls.Count);

				if (!cts.IsCancellationRequested && failedCount == youtubeUrls.Count) {
                    ShowError("No downloads available.");
                }
			})();
			task.ContinueWithOnMainThread(t => {
				if (t.IsFaulted) {
					ShowError(t.Exception.InnerException.ToString());
				}
				DisposeCancellationTokenSource(cts);
			});
		}
	}

    private void AddDownloadOption(Mp3Option option, int priority) {
		int insertIndex = DownloadsPanel.Children.Count;
		for (int i = 0; i < DownloadsPanel.Children.Count; i++) {
			var child = (DownloadOption)DownloadsPanel.Children[i];
			if (priority < child.Priority) {
                insertIndex = i;
                break;
            }
		}

		var downloadOption = new DownloadOption(option, priority);
		DownloadsPanel.Children.Insert(insertIndex, downloadOption);
    }

	public void ShowError(string message) {
		MainThread.BeginInvokeOnMainThread(() => DisplayAlert("Error", message, "OK"));
    }

	private void DisposeCancellationTokenSource(CancellationTokenSource cts) {
		cts.Dispose();
		if (cts == lastCancellationTokenSource) {
			lastCancellationTokenSource = null;
		}
    }
}

class DownloadOption : Grid {
	public int Priority { get; }

    public DownloadOption(Mp3Option option, int priority) {
		Priority = priority;

		double rowHeight = MainPage.Instance.Width / 3.5;
		MaximumHeightRequest = rowHeight;
		ColumnDefinitions = new ColumnDefinitionCollection {
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) },
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            new ColumnDefinition { Width = new GridLength(80) },
        };

		AddColumn(new Image {
			Source = option.ThumbnailUrl,
			HeightRequest = rowHeight,
			MinimumWidthRequest = rowHeight,
		});
        AddColumn(new OptionLabel(option));
        AddColumn(new DownloadButton(option));
	}

	private void AddColumn(IView view) {
		SetColumn(view, Children.Count);
        Children.Add(view);
    }
}

class OptionLabel : Label {
    public OptionLabel(Mp3Option option) {
		const int TITLE_LIMIT = 50;
		var title = option.Title;
		if (title.Length > TITLE_LIMIT) {
			title = title[..TITLE_LIMIT] + "...";
		}
        Text = $"{title}\n{option.Duration}\n{option.Size}";
        LineBreakMode = LineBreakMode.WordWrap;
		HorizontalTextAlignment = TextAlignment.Start;
		VerticalTextAlignment = TextAlignment.Center;
        Padding = new Thickness(10);
    }
}

class DownloadButton : ImageButton {
	private readonly Mp3Option option;

	public DownloadButton(Mp3Option option) {
		this.option = option;
		Clicked += DownloadButton_Clicked;
		BackgroundColor = Color.FromArgb("#FFDDDDDD");
		BorderColor = Color.FromArgb("#FFBBBBBB");
		BorderWidth = 1;
		CornerRadius = 0;
		Source = "dlicon.png";
		Padding = new Thickness(10, 0);
		Margin = new Thickness(0, 4, 0, 0);
    }

	private void DownloadButton_Clicked(object sender, EventArgs e) {
		SetEnabled(false);
		Source = "loading.png";

#if ANDROID
		var path = System.IO.Path.Combine("/storage/emulated/0/Music", option.Title);
#else
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), option.Title);
#endif

		BestMp3ConverterScraper.DownloadMp3(option)
            .ContinueWith(task => {
                int i = 0;
                var finalPath = path + ".mp3";
                while (File.Exists(finalPath)) {
					i++;
					finalPath = path + $" ({i}).mp3";
                }
                using var file = File.Create(finalPath);
                task.Result.Stream.CopyTo(file);
				return new { FinalPath = finalPath };
			})
			.ContinueWithOnMainThread(task => {
				if (task.IsFaulted) {
					MainPage.Instance.ShowError(task.Exception.InnerException.Message);
				} else {
					MainPage.Instance.DisplayAlert("Success", $"Downloaded to {task.Result.FinalPath}", "OK");
				}

				SetEnabled(true);
				Source = "dlicon.png";
			});
	}

	private void SetEnabled(bool enabled) {
		IsEnabled = enabled;
		Opacity = enabled ? 1 : 0.5;
	}
}
