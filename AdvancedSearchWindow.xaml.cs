using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace get_link_manga
{
    public partial class AdvancedSearchWindow : Window
    {
        private readonly MainWindow _mainWindow;

        public AdvancedSearchWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Owner = _mainWindow;

            ApplyLocalization();
            LoadCurrentFilters();
        }

        private void ApplyLocalization()
        {
            bool isVi = _mainWindow._isVietnameseUi;
            lblTitle.Text = isVi ? "TÌM KIẾM NÂNG CAO" : "ADVANCED SEARCH";
            lblInclude.Text = isVi ? "BAO GỒM" : "INCLUDE";
            lblExclude.Text = isVi ? "LOẠI TRỪ" : "EXCLUDE";

            btnApply.Content = isVi ? "ÁP DỤNG" : "APPLY";
            btnClear.Content = isVi ? "XÓA BỘ LỌC" : "CLEAR";
            btnCancel.Content = isVi ? "HỦY" : "CANCEL";
        }

        private void LoadCurrentFilters()
        {
            if (_mainWindow.AdvancedSearchIncludes != null)
            {
                var incs = _mainWindow.AdvancedSearchIncludes;
                if (incs.Count > 0) txtInc1.Text = incs[0];
                if (incs.Count > 1) txtInc2.Text = incs[1];
                if (incs.Count > 2) txtInc3.Text = incs[2];
                if (incs.Count > 3) txtInc4.Text = incs[3];
                if (incs.Count > 4) txtInc5.Text = incs[4];
            }

            if (_mainWindow.AdvancedSearchExcludes != null)
            {
                var excs = _mainWindow.AdvancedSearchExcludes;
                if (excs.Count > 0) txtExc1.Text = excs[0];
                if (excs.Count > 1) txtExc2.Text = excs[1];
                if (excs.Count > 2) txtExc3.Text = excs[2];
                if (excs.Count > 3) txtExc4.Text = excs[3];
                if (excs.Count > 4) txtExc5.Text = excs[4];
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var includes = new List<string>
            {
                txtInc1.Text.Trim(),
                txtInc2.Text.Trim(),
                txtInc3.Text.Trim(),
                txtInc4.Text.Trim(),
                txtInc5.Text.Trim()
            }.Where(s => !string.IsNullOrEmpty(s)).ToList();

            var excludes = new List<string>
            {
                txtExc1.Text.Trim(),
                txtExc2.Text.Trim(),
                txtExc3.Text.Trim(),
                txtExc4.Text.Trim(),
                txtExc5.Text.Trim()
            }.Where(s => !string.IsNullOrEmpty(s)).ToList();

            _mainWindow.AdvancedSearchIncludes = includes;
            _mainWindow.AdvancedSearchExcludes = excludes;

            _mainWindow.ApplyResultsFilter();

            DialogResult = true;
            Close();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtInc1.Text = string.Empty;
            txtInc2.Text = string.Empty;
            txtInc3.Text = string.Empty;
            txtInc4.Text = string.Empty;
            txtInc5.Text = string.Empty;

            txtExc1.Text = string.Empty;
            txtExc2.Text = string.Empty;
            txtExc3.Text = string.Empty;
            txtExc4.Text = string.Empty;
            txtExc5.Text = string.Empty;

            _mainWindow.AdvancedSearchIncludes = new List<string>();
            _mainWindow.AdvancedSearchExcludes = new List<string>();
            _mainWindow.ApplyResultsFilter();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }
    }
}
