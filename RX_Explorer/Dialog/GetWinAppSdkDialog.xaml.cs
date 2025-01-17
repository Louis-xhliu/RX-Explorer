﻿using RX_Explorer.Class;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Store;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace RX_Explorer.Dialog
{
    public sealed partial class GetWinAppSdkDialog : QueueContentDialog
    {
        private readonly InterlockedNoReentryExecution NoReentryExecution;

        public GetWinAppSdkDialog()
        {
            InitializeComponent();
            NoReentryExecution = new InterlockedNoReentryExecution();
        }

        private async void GetActivationCodeButton_Click(object sender, RoutedEventArgs e)
        {
            await NoReentryExecution.ExecuteAsync(async () =>
            {
                ActivateCodeTextBox.PlaceholderForeground = new SolidColorBrush(Colors.Gray);
                ActivateCodeTextBox.PlaceholderText = Globalization.GetString("GetWinAppSdk_Downloading_ActivationCode");
                ActivateCodeTextBox.Text = string.Empty;
                ActivateCodeTextBox.IsEnabled = false;
                ActivateUrlTextBox.Text = string.Empty;
                ActivateUrlTextBox.Visibility = Visibility.Collapsed;
                GetActivationCodeTextContent.Visibility = Visibility.Collapsed;
                GetActivationCodeButton.Visibility = Visibility.Visible;

                try
                {
                    string AccountName = string.Empty;

                    IReadOnlyList<User> CurrentUsers = await User.FindAllAsync();

                    if (CurrentUsers.FirstOrDefault((User) => User.Type == UserType.LocalUser && User.AuthenticationStatus == UserAuthenticationStatus.LocallyAuthenticated) is User CurrentUser)
                    {
                        AccountName = Convert.ToString(await CurrentUser.GetPropertyAsync(KnownUserProperties.AccountName));
                    }

                    if (string.IsNullOrEmpty(AccountName))
                    {
                        ActivateCodeTextBox.PlaceholderForeground = new SolidColorBrush(Colors.OrangeRed);
                        ActivateCodeTextBox.PlaceholderText = Globalization.GetString("GetWinAppSdk_Empty_AccountName");
                    }
                    else
                    {
                        string ReceiptXml = await CurrentApp.GetAppReceiptAsync();

                        if (string.IsNullOrEmpty(ReceiptXml))
                        {
                            ActivateCodeTextBox.PlaceholderForeground = new SolidColorBrush(Colors.OrangeRed);
                            ActivateCodeTextBox.PlaceholderText = Globalization.GetString("GetWinAppSdk_Empty_ReceiptData");
                        }
                        else
                        {
                            try
                            {
                                ValidateRecieptResponseDto ResponseDto = await BackendHelper.ValidateRecieptAsync(AccountName, ReceiptXml);
                                ActivateCodeTextBox.Text = ResponseDto.Content.ActivationCode;
                                ActivateCodeTextBox.IsEnabled = true;
                                ActivateUrlTextBox.Text = ResponseDto.Content.ActivationUrl;
                                ActivateUrlTextBox.Visibility = Visibility.Visible;
                                GetActivationCodeButton.Visibility = Visibility.Collapsed;
                            }
                            catch (Exception ex)
                            {
                                LogTracer.Log(ex, $"Could not download the activation code, reason: {ex.Message}");

                                ActivateCodeTextBox.PlaceholderText = ex.Message;
                                ActivateCodeTextBox.PlaceholderForeground = new SolidColorBrush(Colors.OrangeRed);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, "Could not retrieve the activation code");
                    ActivateCodeTextBox.PlaceholderForeground = new SolidColorBrush(Colors.OrangeRed);
                    ActivateCodeTextBox.PlaceholderText = Globalization.GetString("GetWinAppSdk_Unknown_Exception");
                }
                finally
                {
                    GetActivationCodeTextContent.Visibility = Visibility.Visible;
                }
            });
        }

        private void ActivateCodeCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.Clear();

            DataPackage Package = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };

            Package.SetText(ActivateCodeTextBox.Text);

            Clipboard.SetContent(Package);
        }

        private void ActivateUrlCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.Clear();

            DataPackage Package = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };

            Package.SetText(ActivateUrlTextBox.Text);

            Clipboard.SetContent(Package);
        }
    }
}
