﻿using System;
using System.Windows.Forms;

namespace SharpCrop.Provider.Forms
{
    /// <summary>
    /// FolderForm is used to help the user select a directory.
    /// </summary>
    public partial class FolderForm : Form
    {
        private Action<string> onResult;

        /// <summary>
        /// Simple folder chooser form.
        /// </summary>
        public FolderForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Register for callback.
        /// </summary>
        /// <param name="onResult"></param>
        public void OnResult(Action<string> onResult)
        {
            this.onResult = onResult;
        }

        /// <summary>
        /// Start OS specific folder browser when the browse button was clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnBrowse(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                folderBox.Text = folderBrowserDialog.SelectedPath;
            }
        }

        /// <summary>
        /// Call callback with the result.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnSubmit(object sender, EventArgs e)
        {
            onResult(folderBox.Text);
        }
    }
}