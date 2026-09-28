using BKE_MediaTools;

namespace BKE_MediaTools
{
    partial class BKE_RenderDock
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BKE_RenderDock));
            SuspendLayout();
            // 
            // BKE_RenderDock
            // 
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackgroundImage = (Image)resources.GetObject("$this.BackgroundImage");
            BackgroundImageLayout = ImageLayout.Stretch;
            ClientSize = new Size(449, 450);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "BKE_RenderDock";
            Text = "Render Dock";
            Load += BKE_RenderDock_Load;
            MouseDoubleClick += BKE_RenderDock_MouseDoubleClick;
            ResumeLayout(false);
        }

        #endregion
    }
}