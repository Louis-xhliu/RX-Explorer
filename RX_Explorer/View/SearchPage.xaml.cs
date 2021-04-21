﻿using RX_Explorer.Class;
using RX_Explorer.SeparateWindow.PropertyWindow;
using ShareClassLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.WindowManagement;
using Windows.UI.WindowManagement.Preview;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using Package = Windows.ApplicationModel.Package;

namespace RX_Explorer
{
    public sealed partial class SearchPage : Page
    {
        private readonly ObservableCollection<FileSystemStorageItemBase> SearchResult = new ObservableCollection<FileSystemStorageItemBase>();
        private WeakReference<FileControl> WeakToFileControl;
        private CancellationTokenSource Cancellation;
        private readonly Dictionary<SortTarget, SortDirection> SortMap = new Dictionary<SortTarget, SortDirection>
        {
            {SortTarget.Name,SortDirection.Ascending },
            {SortTarget.Type,SortDirection.Ascending },
            {SortTarget.ModifiedTime,SortDirection.Ascending },
            {SortTarget.Size,SortDirection.Ascending },
            {SortTarget.Path,SortDirection.Ascending }
        };


        public SearchPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e?.Parameter is Tuple<WeakReference<FileControl>, SearchCategory, bool?, bool?, bool?, uint?> Parameters)
            {
                CoreWindow.GetForCurrentThread().KeyDown += SearchPage_KeyDown;

                if (e.NavigationMode == NavigationMode.New)
                {
                    WeakToFileControl = Parameters.Item1;
                    await Initialize(Parameters.Item2, Parameters.Item3.GetValueOrDefault(), Parameters.Item4.GetValueOrDefault(), Parameters.Item5.GetValueOrDefault(), Parameters.Item6.GetValueOrDefault()).ConfigureAwait(false);
                }
            }
        }

        private void SearchPage_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            SearchCommandFlyout.Hide();

            CoreVirtualKeyStates CtrlState = sender.GetKeyState(VirtualKey.Control);

            switch (args.VirtualKey)
            {
                case VirtualKey.L when CtrlState.HasFlag(CoreVirtualKeyStates.Down):
                    {
                        Location_Click(null, null);
                        break;
                    }
            }
        }

        private async Task Initialize(SearchCategory Category, bool IngoreCase, bool IncludeRegex, bool GlobleSearch, uint MaxCount)
        {
            HasItem.Visibility = Visibility.Collapsed;

            try
            {
                Cancellation = new CancellationTokenSource();

                if (WeakToFileControl.TryGetTarget(out FileControl Control))
                {
                    string SearchTarget = Control.GlobeSearch.Text;
                    FileSystemStorageFolder CurrentFolder = Control.CurrentPresenter.CurrentFolder;

                    SearchStatus.Text = Globalization.GetString("SearchProcessingText");
                    SearchStatusBar.Visibility = Visibility.Visible;

                    switch (Category)
                    {
                        case SearchCategory.BuiltInEngine_Deep:
                        case SearchCategory.BuiltInEngine_Shallow:
                            {
                                await foreach (FileSystemStorageItemBase Item in CurrentFolder.SearchAsync(SearchTarget, Category == SearchCategory.BuiltInEngine_Deep, SettingControl.IsDisplayHiddenItem, SettingControl.IsDisplayProtectedSystemItems, IncludeRegex, IngoreCase, Cancellation.Token))
                                {
                                    if (Cancellation.IsCancellationRequested)
                                    {
                                        HasItem.Visibility = Visibility.Visible;
                                        break;
                                    }
                                    else
                                    {
                                        SearchResult.Insert(SortCollectionGenerator.SearchInsertLocation(SearchResult, Item, SortTarget.Name, SortDirection.Ascending), Item);
                                    }
                                }

                                if (SearchResult.Count == 0)
                                {
                                    HasItem.Visibility = Visibility.Visible;
                                }

                                break;
                            }
                        case SearchCategory.EverythingEngine:
                            {
                                using (FullTrustProcessController.ExclusiveUsage Exclusive = await FullTrustProcessController.GetAvailableController())
                                {
                                    IReadOnlyList<FileSystemStorageItemBase> SearchItems = await Exclusive.Controller.SearchByEverythingAsync(GlobleSearch ? string.Empty : CurrentFolder.Path, SearchTarget, IncludeRegex, IngoreCase, MaxCount);

                                    if (SearchItems.Count == 0)
                                    {
                                        HasItem.Visibility = Visibility.Visible;
                                    }
                                    else
                                    {
                                        foreach (FileSystemStorageItemBase Item in SortCollectionGenerator.GetSortedCollection(SearchItems, SortTarget.Name, SortDirection.Ascending))
                                        {
                                            SearchResult.Add(Item);
                                        }
                                    }
                                }

                                break;
                            }
                    }

                    SearchStatus.Text = Globalization.GetString("SearchCompletedText");
                    SearchStatusBar.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An error was threw in {nameof(Initialize)}");
            }
            finally
            {
                Cancellation.Dispose();
                Cancellation = null;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            CoreWindow.GetForCurrentThread().KeyDown -= SearchPage_KeyDown;
            Cancellation?.Cancel();

            if (e.NavigationMode == NavigationMode.Back)
            {
                SearchResult.Clear();
            }
        }

        private async void Location_Click(object sender, RoutedEventArgs e)
        {
            if (SearchResultList.SelectedItem is FileSystemStorageItemBase Item)
            {
                try
                {
                    string ParentFolderPath = Path.GetDirectoryName(Item.Path);

                    if (WeakToFileControl.TryGetTarget(out FileControl Control))
                    {
                        Frame.GoBack();

                        await Control.CurrentPresenter.DisplayItemsInFolder(ParentFolderPath);

                        await JumpListController.Current.AddItemAsync(JumpListGroup.Recent, ParentFolderPath);

                        if (Control.CurrentPresenter.FileCollection.FirstOrDefault((SItem) => SItem == Item) is FileSystemStorageItemBase Target)
                        {
                            Control.CurrentPresenter.ItemPresenter.ScrollIntoView(Target);
                            Control.CurrentPresenter.SelectedItem = Target;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, $"An error was threw in {nameof(Location_Click)}");

                    QueueContentDialog dialog = new QueueContentDialog
                    {
                        Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                        Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                        CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                    };

                    _ = await dialog.ShowAsync();
                }
            }
        }

        private async void Attribute_Click(object sender, RoutedEventArgs e)
        {
            if (SearchResultList.SelectedItem is FileSystemStorageItemBase Item)
            {
                AppWindow NewWindow = await AppWindow.TryCreateAsync();
                NewWindow.RequestSize(new Size(420, 600));
                NewWindow.RequestMoveRelativeToCurrentViewContent(new Point(Window.Current.Bounds.Width / 2 - 200, Window.Current.Bounds.Height / 2 - 300));
                NewWindow.PersistedStateId = "Properties";
                NewWindow.Title = Globalization.GetString("Properties_Window_Title");
                NewWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                NewWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                NewWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

                ElementCompositionPreview.SetAppWindowContent(NewWindow, new PropertyBase(NewWindow, Item));
                WindowManagementPreview.SetPreferredMinSize(NewWindow, new Size(420, 600));

                await NewWindow.TryShowAsync();
            }
        }

        private void SearchResultList_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItemBase Context)
                {
                    SearchResultList.ContextFlyout = SearchCommandFlyout;
                    SearchResultList.SelectedItem = Context;
                }
                else
                {
                    SearchResultList.ContextFlyout = null;
                }

                e.Handled = true;
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (SearchResultList.SelectedItem is FileSystemStorageItemBase SelectItem)
            {
                DataPackage Package = new DataPackage();
                Package.SetText(SelectItem.Path);
                Clipboard.SetContent(Package);
            }
        }

        private void SearchResultList_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is FileSystemStorageItemBase Context)
                {
                    SearchResultList.ContextFlyout = SearchCommandFlyout;
                    SearchResultList.SelectedItem = Context;
                }
                else
                {
                    SearchResultList.ContextFlyout = null;
                }

                e.Handled = true;
            }
        }

        private void SearchResultList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            args.RegisterUpdateCallback(async (s, e) =>
            {
                if (e.Item is FileSystemStorageItemBase Item)
                {
                    await Item.LoadMorePropertiesAsync().ConfigureAwait(false);
                }
            });
        }

        private void ListHeaderSize_Click(object sender, RoutedEventArgs e)
        {
            if (SortMap[SortTarget.Size] == SortDirection.Ascending)
            {
                SortMap[SortTarget.Size] = SortDirection.Descending;
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Path] = SortDirection.Ascending;
                SortMap[SortTarget.Type] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.Size, SortDirection.Descending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
            else
            {
                SortMap[SortTarget.Size] = SortDirection.Ascending;
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Path] = SortDirection.Ascending;
                SortMap[SortTarget.Type] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.Size, SortDirection.Ascending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
        }

        private void ListHeaderType_Click(object sender, RoutedEventArgs e)
        {
            if (SortMap[SortTarget.Type] == SortDirection.Ascending)
            {
                SortMap[SortTarget.Type] = SortDirection.Descending;
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Path] = SortDirection.Ascending;
                SortMap[SortTarget.Size] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.Type, SortDirection.Descending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
            else
            {
                SortMap[SortTarget.Type] = SortDirection.Ascending;
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Path] = SortDirection.Ascending;
                SortMap[SortTarget.Size] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.Type, SortDirection.Ascending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
        }

        private void ListHeaderModifyDate_Click(object sender, RoutedEventArgs e)
        {
            if (SortMap[SortTarget.ModifiedTime] == SortDirection.Ascending)
            {
                SortMap[SortTarget.ModifiedTime] = SortDirection.Descending;
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.Type] = SortDirection.Ascending;
                SortMap[SortTarget.Path] = SortDirection.Ascending;
                SortMap[SortTarget.Size] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.ModifiedTime, SortDirection.Descending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
            else
            {
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.Type] = SortDirection.Ascending;
                SortMap[SortTarget.Path] = SortDirection.Ascending;
                SortMap[SortTarget.Size] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.ModifiedTime, SortDirection.Ascending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
        }

        private void ListHeaderPath_Click(object sender, RoutedEventArgs e)
        {
            if (SortMap[SortTarget.Path] == SortDirection.Ascending)
            {
                SortMap[SortTarget.Path] = SortDirection.Descending;
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.Type] = SortDirection.Ascending;
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Size] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.Path, SortDirection.Descending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
            else
            {
                SortMap[SortTarget.Path] = SortDirection.Ascending;
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.Type] = SortDirection.Ascending;
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Size] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.Path, SortDirection.Ascending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
        }

        private void ListHeaderName_Click(object sender, RoutedEventArgs e)
        {
            if (SortMap[SortTarget.Name] == SortDirection.Ascending)
            {
                SortMap[SortTarget.Name] = SortDirection.Descending;
                SortMap[SortTarget.Type] = SortDirection.Ascending;
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Size] = SortDirection.Ascending;
                SortMap[SortTarget.Path] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.Name, SortDirection.Descending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
            else
            {
                SortMap[SortTarget.Name] = SortDirection.Ascending;
                SortMap[SortTarget.Type] = SortDirection.Ascending;
                SortMap[SortTarget.ModifiedTime] = SortDirection.Ascending;
                SortMap[SortTarget.Size] = SortDirection.Ascending;
                SortMap[SortTarget.Path] = SortDirection.Ascending;

                FileSystemStorageItemBase[] SortResult = SortCollectionGenerator.GetSortedCollection(SearchResult, SortTarget.Name, SortDirection.Ascending).ToArray();

                SearchResult.Clear();

                foreach (FileSystemStorageItemBase Item in SortResult)
                {
                    SearchResult.Add(Item);
                }
            }
        }

        private bool TryOpenInternally(FileSystemStorageFile File)
        {
            Type InternalType = File.Type.ToLower() switch
            {
                ".jpg" or ".png" or ".bmp" => typeof(PhotoViewer),
                ".mkv" or ".mp4" or ".mp3" or
                ".flac" or ".wma" or ".wmv" or
                ".m4a" or ".mov" or ".alac" => typeof(MediaPlayer),
                ".txt" => typeof(TextViewer),
                ".pdf" => typeof(PdfReader),
                _ => null
            };

            if (InternalType != null)
            {
                NavigationTransitionInfo NavigationTransition = AnimationController.Current.IsEnableAnimation
                                                            ? new DrillInNavigationTransitionInfo()
                                                            : new SuppressNavigationTransitionInfo();

                Frame.Navigate(InternalType, File, NavigationTransition);

                return true;
            }
            else
            {
                return false;
            }
        }

        private async void SearchResultList_DoubleTapped(object sender, Windows.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement).DataContext is FileSystemStorageItemBase Item)
            {
                await LaunchSelectedItem(Item);
            }
        }

        private async Task LaunchSelectedItem(FileSystemStorageItemBase Item)
        {
            try
            {
                switch (Item)
                {
                    case FileSystemStorageFile File:
                        {
                            if (!await FileSystemStorageItemBase.CheckExistAsync(File.Path))
                            {
                                QueueContentDialog Dialog = new QueueContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_LocateFileFailure_Content"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                };

                                _ = await Dialog.ShowAsync();

                                return;
                            }

                            switch (File.Type.ToLower())
                            {
                                case ".exe":
                                case ".bat":
                                case ".msi":
                                    {
                                        using (FullTrustProcessController.ExclusiveUsage Exclusive = await FullTrustProcessController.GetAvailableController())
                                        {
                                            if (!await Exclusive.Controller.RunAsync(File.Path, Path.GetDirectoryName(File.Path)))
                                            {
                                                QueueContentDialog Dialog = new QueueContentDialog
                                                {
                                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                    Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                };

                                                await Dialog.ShowAsync();
                                            }
                                        }

                                        break;
                                    }
                                case ".msc":
                                    {
                                        using (FullTrustProcessController.ExclusiveUsage Exclusive = await FullTrustProcessController.GetAvailableController())
                                        {
                                            if (!await Exclusive.Controller.RunAsync("powershell.exe", string.Empty, WindowState.Normal, false, true, false, "-Command", File.Path))
                                            {
                                                QueueContentDialog Dialog = new QueueContentDialog
                                                {
                                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                    Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                };

                                                await Dialog.ShowAsync();
                                            }
                                        }

                                        break;
                                    }
                                case ".lnk":
                                    {
                                        if (File is LinkStorageFile LinkItem)
                                        {
                                            if (LinkItem.LinkType == ShellLinkType.Normal)
                                            {
                                                switch (await FileSystemStorageItemBase.OpenAsync(LinkItem.LinkTargetPath))
                                                {
                                                    case FileSystemStorageFolder:
                                                        {
                                                            if (WeakToFileControl.TryGetTarget(out FileControl Control))
                                                            {
                                                                Frame.GoBack();

                                                                await Control.CurrentPresenter.DisplayItemsInFolder(LinkItem.LinkTargetPath);

                                                                await JumpListController.Current.AddItemAsync(JumpListGroup.Recent, LinkItem.LinkTargetPath);

                                                                if (Control.CurrentPresenter.FileCollection.FirstOrDefault((SItem) => SItem == LinkItem) is FileSystemStorageItemBase Target)
                                                                {
                                                                    Control.CurrentPresenter.ItemPresenter.ScrollIntoView(Target);
                                                                    Control.CurrentPresenter.SelectedItem = Target;
                                                                }
                                                            }

                                                            break;
                                                        }
                                                    case FileSystemStorageFile:
                                                        {
                                                            await LinkItem.LaunchAsync();
                                                            break;
                                                        }
                                                }
                                            }
                                            else
                                            {
                                                await LinkItem.LaunchAsync();
                                            }
                                        }

                                        break;
                                    }
                                case ".url":
                                    {
                                        if (File is UrlStorageFile UrlItem)
                                        {
                                            await UrlItem.LaunchAsync();
                                        }

                                        break;
                                    }
                                default:
                                    {
                                        string AdminExecutablePath = await SQLite.Current.GetDefaultProgramPickerRecordAsync(File.Type);

                                        if (string.IsNullOrEmpty(AdminExecutablePath) || AdminExecutablePath == Package.Current.Id.FamilyName)
                                        {
                                            if (!TryOpenInternally(File))
                                            {
                                                if (await File.GetStorageItemAsync() is StorageFile SFile)
                                                {
                                                    if (!await Launcher.LaunchFileAsync(SFile))
                                                    {
                                                        using (FullTrustProcessController.ExclusiveUsage Exclusive = await FullTrustProcessController.GetAvailableController())
                                                        {
                                                            if (!await Exclusive.Controller.RunAsync(File.Path))
                                                            {
                                                                QueueContentDialog Dialog = new QueueContentDialog
                                                                {
                                                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                                    Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                                };

                                                                await Dialog.ShowAsync();
                                                            }
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    using (FullTrustProcessController.ExclusiveUsage Exclusive = await FullTrustProcessController.GetAvailableController())
                                                    {
                                                        if (!await Exclusive.Controller.RunAsync(File.Path))
                                                        {
                                                            QueueContentDialog Dialog = new QueueContentDialog
                                                            {
                                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                                Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                            };

                                                            await Dialog.ShowAsync();
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (Path.IsPathRooted(AdminExecutablePath))
                                            {
                                                using (FullTrustProcessController.ExclusiveUsage Exclusive = await FullTrustProcessController.GetAvailableController())
                                                {
                                                    if (!await Exclusive.Controller.RunAsync(AdminExecutablePath, Path.GetDirectoryName(AdminExecutablePath), Parameters: File.Path))
                                                    {
                                                        QueueContentDialog Dialog = new QueueContentDialog
                                                        {
                                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                            Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                        };

                                                        await Dialog.ShowAsync();
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                if ((await Launcher.FindFileHandlersAsync(File.Type)).FirstOrDefault((Item) => Item.PackageFamilyName == AdminExecutablePath) is AppInfo Info)
                                                {
                                                    if (await File.GetStorageItemAsync() is StorageFile InnerFile)
                                                    {
                                                        if (!await Launcher.LaunchFileAsync(InnerFile, new LauncherOptions { TargetApplicationPackageFamilyName = Info.PackageFamilyName, DisplayApplicationPicker = false }))
                                                        {
                                                            QueueContentDialog Dialog = new QueueContentDialog
                                                            {
                                                                Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                                Content = Globalization.GetString("QueueDialog_LaunchFailed_Content"),
                                                                CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                            };

                                                            await Dialog.ShowAsync();
                                                        }
                                                    }
                                                    else
                                                    {
                                                        QueueContentDialog Dialog = new QueueContentDialog
                                                        {
                                                            Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                                            Content = Globalization.GetString("QueueDialog_UnableAccessFile_Content"),
                                                            CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                                        };

                                                        _ = await Dialog.ShowAsync();
                                                    }
                                                }
                                            }
                                        }

                                        break;
                                    }
                            }

                            break;
                        }
                    case FileSystemStorageFolder Folder:
                        {
                            if (await FileSystemStorageItemBase.CheckExistAsync(Folder.Path))
                            {
                                if (WeakToFileControl.TryGetTarget(out FileControl Control))
                                {
                                    Frame.GoBack();

                                    await Control.CurrentPresenter.DisplayItemsInFolder(Folder);

                                    await JumpListController.Current.AddItemAsync(JumpListGroup.Recent, Folder.Path);

                                    if (Control.CurrentPresenter.FileCollection.FirstOrDefault((SItem) => SItem == Folder) is FileSystemStorageItemBase Target)
                                    {
                                        Control.CurrentPresenter.ItemPresenter.ScrollIntoView(Target);
                                        Control.CurrentPresenter.SelectedItem = Target;
                                    }
                                }
                            }
                            else
                            {
                                QueueContentDialog Dialog = new QueueContentDialog
                                {
                                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                                };

                                _ = await Dialog.ShowAsync();
                            }

                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An error was threw in {nameof(LaunchSelectedItem)}");

                QueueContentDialog dialog = new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_LocateFolderFailure_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                };

                _ = await dialog.ShowAsync();
            }
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            if (SearchResultList.SelectedItem is FileSystemStorageItemBase Item)
            {
                await LaunchSelectedItem(Item);
            }
        }
    }
}
