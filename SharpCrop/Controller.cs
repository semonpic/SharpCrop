﻿using SharpCrop.Forms;
using SharpCrop.Provider;
using SharpCrop.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SharpCrop
{
    /// <summary>
    /// Controller is responsible for the loading of the application. It will also
    /// manage image capturing and uploading.
    /// </summary>
    public class Controller : ApplicationContext
    {
        private readonly Dictionary<string, IProvider> loadedProviders = new Dictionary<string, IProvider>();
        private readonly List<Form> cropForms = new List<Form>();
        private readonly Form configForm;

        /// <summary>
        /// Construct a new Controller class.
        /// </summary>
        public Controller()
        {
            // Create a new ConfigForm
            configForm = new ConfigForm(this);
            configForm.FormClosed += (s, e) => Application.Exit();
            configForm.Load += async (s, e) => await InitProviders();

            // If there are any loaded providers, the config will be hidden
            // Else the config will be closed (along with the whole application)
            configForm.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing && loadedProviders.Count > 0)
                {
                    e.Cancel = true;
                    configForm.Hide();
                }
            };

            // Show crop forms if the config is hidden
            configForm.VisibleChanged += (s, e) =>
            {
                if (!configForm.Visible)
                {
                    ShowCrop();
                }
            };

            // Create a CropForm for every screen and show them
            for (var i = 0; i < Screen.AllScreens.Length; i++)
            {
                var screen = Screen.AllScreens[i];
                var form = new CropForm(this, screen.Bounds, i);

                form.FormClosed += (s, e) => Application.Exit();

                cropForms.Add(form);
            }

            // If LoadOnStartup is enabled, init providers on the load of the first CropForm
            if(ConfigHelper.Memory.LoadOnStartup)
            {
                cropForms[0].Load += async (s, e) => await InitProviders();
            }

            // Show settings if no providers gonna be loaded
            if(ConfigHelper.Memory.SafeProviders.Count > 0)
            {
                ShowCrop();
            }
            else
            {
                configForm.Show();
            }
        }

        /// <summary>
        /// Capture one Bitmap.
        /// </summary>
        /// <param name="region"></param>
        /// <param name="offset"></param>
        public async void CaptureImage(Rectangle region, Point offset)
        {
            using (var stream = new MemoryStream())
            {
                // Capture the frame
                using (var bitmap = CaptureHelper.GetBitmap(region, offset))
                {
                    bitmap.Save(stream, ConfigHelper.Memory.ImageFormatType);
                }

                // Generate filename and start the upload(s)
                var name = $"{DateTime.Now:yyyy_MM_dd_HH_mm_ss}.{ConfigHelper.Memory.SafeImageFormat}";
                var url = await UploadAll(name, stream);

                CompleteCapture(url);
            }
        }

        /// <summary>
        /// Capture a lot of bitmaps and convert them to gif.
        /// </summary>
        /// <param name="region"></param>
        /// <param name="offset"></param>
        public async void CaptureGif(Rectangle region, Point offset)
        {
            MemoryStream stream;
            var toast = -1;

            // Create a new toast which closing event gonna stop the recording
            toast = ToastFactory.Create("Click here to stop!", Color.OrangeRed, 0, () =>
            {
                toast = ToastFactory.Create("Encoding...", 0);
                VideoFactory.Stop();
            });

            // Use Mpeg if enabled
            if (ConfigHelper.Memory.EnableMpeg)
            {
                stream = await VideoFactory.RecordMpeg(region, offset);
            }
            else
            {
                stream = await VideoFactory.RecordGif(region, offset);
            }

            ToastFactory.Remove(toast);

            // Generate filename and start the upload(s)
            toast = ToastFactory.Create($"Uploading... ({(double)stream.Length / (1024 * 1024):0.00} MB)", 0);

            var name = $"{DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss")}.{(ConfigHelper.Memory.EnableMpeg ? "mp4" : "gif")}";
            var url = await UploadAll(name, stream);

            ToastFactory.Remove(toast);
            stream.Dispose();

            CompleteCapture(url);
        }

        /// <summary>
        /// Upload the given stream with all loaded providers.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="stream"></param>
        /// <returns></returns>
        private async Task<string> UploadAll(string name, MemoryStream stream)
        {
            // Try to load saved providers
            if (!await InitProviders())
            {
                File.WriteAllBytes(name, stream.ToArray());
                return null;
            }

            var uploads = new Dictionary<string, Task<string>>();

            string result = null;
            string last = null;

            // Run the uploads async
            foreach (var p in loadedProviders)
            {
                if (p.Value != null)
                {
                    uploads[p.Key] = p.Value.Upload(name, stream);
                }
            }

            // Wait for the uploads to finish and get the chosen URL
            foreach (var p in uploads)
            {
                var url = await p.Value;

                if (string.IsNullOrEmpty(url))
                {
                    ToastFactory.Create($"Upload failed using \"{p.Key}\" provider!");
                }
                else
                {
                    last = url;

                    if (p.Key == ConfigHelper.Memory.ProviderToCopy)
                    {
                        result = url;
                    }
                }
            }

            // If the chosen URL was not found (or null), use the URL of the last successful one
            return string.IsNullOrEmpty(result) ? last : result;
        }

        /// <summary>
        /// Init providers with the previously saved states.
        /// </summary>
        /// <returns>Returns true if at least one provider was successfully loaded.</returns>
        public async Task<bool> InitProviders()
        {
            var tasks = new List<Task<bool>>();
            var result = false;

            foreach (var p in ConfigHelper.Memory.SafeProviders)
            {
                tasks.Add(LoadProvider(p.Key, p.Value));
            }

            foreach (var p in tasks)
            {
                result = await p ? true : result;
            }

            return result;
        }

        /// <summary>
        /// Register the given provider with a registration form and load it.
        /// </summary>
        /// <param name="name"></param>
        public async void RegisterProvider(string name)
        {
            var provider = await GetProvider(name);

            if (provider != null)
            {
                loadedProviders[name] = provider;
            }
        }

        /// <summary>
        /// Try to load the given provider with the saved state silently - if it failes, there will
        /// be no registration form. The failed providers gonna be put into the dictionary with 
        /// a null IProvider.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="savedState"></param>
        public async Task<bool> LoadProvider(string name, string savedState)
        {
            // If the provider was loaded successfully, return with true
            if (loadedProviders.ContainsKey(name))
            {
                return loadedProviders[name] != null;
            }

            var provider = await GetProvider(name, savedState, false);

            loadedProviders[name] = provider;

            return provider != null;
        }

        /// <summary>
        /// Unregister a provider.
        /// </summary>
        /// <param name="name"></param>
        public void ClearProvider(string name)
        {
            // Remove from the loaded providers
            if (loadedProviders.ContainsKey(name))
            {
                loadedProviders.Remove(name);
            }

            // Remove from the configuration file
            if (ConfigHelper.Memory.SafeProviders.ContainsKey(name))
            {
                ConfigHelper.Memory.SafeProviders.Remove(name);
            }
        }

        /// <summary>
        /// Get an IProvider object from the given provider module.
        /// </summary>
        /// <param name="name">The name of the provider, defined in the Constants.</param>
        /// <param name="savedState">A saved state which can be null.</param>
        /// <param name="showForm">Show the registration form or not.</param>
        /// <returns>Return a Provider state (usually json in base64), if the was an error, the result will be null.</returns>
        private async Task<IProvider> GetProvider(string name, string savedState = null, bool showForm = true)
        {
            if (!Constants.AvailableProviders.ContainsKey(name))
            {
                return null;
            }

            // Translate name into a real instance and try to load the provider form the given saved state
            var provider = (IProvider)Activator.CreateInstance(Constants.AvailableProviders[name]);
            var state = await provider.Register(savedState, showForm);

            if (state == null)
            {
                ToastFactory.Create($"Failed to register \"{name}\" provider!");
                return null;
            }

            // If the token is not changed, there was no new registration
            if (state != savedState)
            {
                ConfigHelper.Memory.SafeProviders[name] = state;
                ToastFactory.Create($"Successfully registered \"{name}\" provider!");
            }

            return provider;
        }

        /// <summary>
        /// Show the end notifcation. If the url is null, the text gonna tell the user, that the upload has failed.
        /// </summary>
        /// <param name="url"></param>
        private void CompleteCapture(string url = null)
        {
            if (!ConfigHelper.Memory.NoCopy && !string.IsNullOrEmpty(url))
            {
                Clipboard.SetText(url);

#if __MonoCS__
                var form = new CopyForm(url);
                form.FormClosed += (object sender, FormClosedEventArgs e) => Application.Exit();
                form.Show();
#endif
            }

            ToastFactory.Create(string.IsNullOrEmpty(url) ? "Upload failed! File was saved locally." : "Upload completed!", 3000, () =>
            {
#if !__MonoCS__
                Application.Exit();
#endif
            });
        }

        /// <summary>
        /// Hide every CropForm - if more than one monitor is used.
        /// </summary>
        public void HideCrop()
        {
            cropForms.ForEach(e => e.Hide());
        }

        /// <summary>
        /// Show every CropForm.
        /// </summary>
        public void ShowCrop()
        {
            cropForms.ForEach(e => e.Show());
        }

        /// <summary>
        /// Protect list from external modification.
        /// </summary>
        public IReadOnlyDictionary<string, IProvider> LoadedProviders => loadedProviders;

        /// <summary>
        /// Protect list from external modification.
        /// </summary>
        public IReadOnlyList<Form> CropForms => cropForms;

        /// <summary>
        /// Protect variable from external modification.
        /// </summary>
        public Form ConfigForm => configForm;
    }
}