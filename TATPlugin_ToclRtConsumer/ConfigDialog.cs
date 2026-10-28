using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

internal sealed class ConfigDialog : Form
{
    private readonly TextBox m_tmf = new TextBox();
    private readonly TextBox m_pdb = new TextBox();
    private readonly TextBox m_out = new TextBox();
    private readonly CheckedListBox m_sessions = new CheckedListBox();
    private readonly TextBox m_custom = new TextBox();
    private readonly CheckBox m_openFirst = new CheckBox();

    public ConfigDialog(RtSettings settings)
    {
        Text = "TOCL RT consumer - Configure";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 440);

        AddPathRow("TMF folder (WPP):", m_tmf, 10, true);
        AddPathRow("PDB file/folder:", m_pdb, 50, true);
        AddPathRow("Output folder:", m_out, 90, true);

        Label sessionsLabel = new Label();
        sessionsLabel.Text = "Sessions (none checked = all known sessions):";
        sessionsLabel.SetBounds(10, 130, 400, 18);
        Controls.Add(sessionsLabel);

        m_sessions.SetBounds(10, 150, 540, 140);
        m_sessions.CheckOnClick = true;
        m_sessions.MultiColumn = true;
        Controls.Add(m_sessions);

        Label customLabel = new Label();
        customLabel.Text = "Additional session names (separated by ;):";
        customLabel.SetBounds(10, 298, 400, 18);
        Controls.Add(customLabel);
        m_custom.SetBounds(10, 318, 540, 22);
        Controls.Add(m_custom);

        m_openFirst.Text = "Open the first attached session log in TextAnalysisTool.NET";
        m_openFirst.SetBounds(10, 352, 450, 20);
        Controls.Add(m_openFirst);

        Button ok = new Button();
        ok.Text = "OK";
        ok.DialogResult = DialogResult.OK;
        ok.SetBounds(390, 400, 75, 26);
        Controls.Add(ok);
        Button cancel = new Button();
        cancel.Text = "Cancel";
        cancel.DialogResult = DialogResult.Cancel;
        cancel.SetBounds(475, 400, 75, 26);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        m_tmf.Text = settings.TmfPath;
        m_pdb.Text = settings.PdbPath;
        m_out.Text = settings.OutputFolder;
        m_openFirst.Checked = settings.OpenFirstFile;

        List<string> extra = new List<string>();
        foreach (string name in RtSettings.KnownSessions)
        {
            m_sessions.Items.Add(name, ContainsIgnoreCase(settings.Sessions, name));
        }

        foreach (string name in settings.Sessions)
        {
            if (!ContainsIgnoreCase(new List<string>(RtSettings.KnownSessions), name))
            {
                extra.Add(name);
            }
        }

        m_custom.Text = string.Join(";", extra.ToArray());
    }

    public void ApplyTo(RtSettings settings)
    {
        settings.TmfPath = m_tmf.Text.Trim();
        settings.PdbPath = m_pdb.Text.Trim();
        settings.OutputFolder = m_out.Text.Trim().Length > 0 ? m_out.Text.Trim() : settings.OutputFolder;
        settings.OpenFirstFile = m_openFirst.Checked;
        settings.Sessions.Clear();
        foreach (object item in m_sessions.CheckedItems)
        {
            settings.Sessions.Add((string)item);
        }

        foreach (string name in m_custom.Text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = name.Trim();
            if (trimmed.Length > 0 && !ContainsIgnoreCase(settings.Sessions, trimmed))
            {
                settings.Sessions.Add(trimmed);
            }
        }
    }

    private static bool ContainsIgnoreCase(List<string> list, string value)
    {
        foreach (string item in list)
        {
            if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void AddPathRow(string caption, TextBox box, int top, bool browse)
    {
        Label label = new Label();
        label.Text = caption;
        label.SetBounds(10, top, 300, 18);
        Controls.Add(label);
        box.SetBounds(10, top + 18, 460, 22);
        Controls.Add(box);
        Button button = new Button();
        button.Text = "Browse...";
        button.SetBounds(478, top + 16, 72, 26);
        button.Click += delegate
        {
            if (object.ReferenceEquals(box, m_pdb))
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Select a PDB file (Cancel to pick a folder)";
                    dialog.Filter = "PDB files (*.pdb)|*.pdb|All files (*.*)|*.*";
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        box.Text = dialog.FileName;
                        return;
                    }
                }
            }

            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = box.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    box.Text = dialog.SelectedPath;
                }
            }
        };
        Controls.Add(button);
    }
}
