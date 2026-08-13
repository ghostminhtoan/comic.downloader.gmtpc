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
        private readonly DuplicateWindow _duplicateWindow;

        public AdvancedSearchWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Owner = _mainWindow;

            ApplyLocalization();
            LoadCurrentFilters();
        }

        public AdvancedSearchWindow(DuplicateWindow duplicateWindow)
        {
            InitializeComponent();
            _duplicateWindow = duplicateWindow;
            Owner = _duplicateWindow;

            ApplyLocalization();
            LoadCurrentFilters();
        }

        private void ApplyLocalization()
        {
            bool isVi = _mainWindow != null ? _mainWindow._isVietnameseUi : _duplicateWindow._mainWindow._isVietnameseUi;
            lblTitle.Text = isVi ? "TÌM KIẾM NÂNG CAO" : "ADVANCED SEARCH";
            lblInclude.Text = isVi ? "BAO GỒM" : "INCLUDE";
            lblExclude.Text = isVi ? "LOẠI TRỪ" : "EXCLUDE";

            btnApply.Content = isVi ? "ÁP DỤNG" : "APPLY";
            btnClear.Content = isVi ? "XÓA BỘ LỌC" : "CLEAR";
            btnCancel.Content = isVi ? "HỦY" : "CANCEL";
        }

        private void LoadCurrentFilters()
        {
            var includes = _mainWindow != null ? _mainWindow.AdvancedSearchIncludes : _duplicateWindow.AdvancedSearchIncludes;
            var excludes = _mainWindow != null ? _mainWindow.AdvancedSearchExcludes : _duplicateWindow.AdvancedSearchExcludes;

            if (includes != null)
            {
                if (includes.Count > 0) txtInc1.Text = includes[0];
                if (includes.Count > 1) txtInc2.Text = includes[1];
                if (includes.Count > 2) txtInc3.Text = includes[2];
                if (includes.Count > 3) txtInc4.Text = includes[3];
                if (includes.Count > 4) txtInc5.Text = includes[4];
            }

            if (excludes != null)
            {
                if (excludes.Count > 0) txtExc1.Text = excludes[0];
                if (excludes.Count > 1) txtExc2.Text = excludes[1];
                if (excludes.Count > 2) txtExc3.Text = excludes[2];
                if (excludes.Count > 3) txtExc4.Text = excludes[3];
                if (excludes.Count > 4) txtExc5.Text = excludes[4];
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

            if (_mainWindow != null)
            {
                _mainWindow.AdvancedSearchIncludes = includes;
                _mainWindow.AdvancedSearchExcludes = excludes;
                _mainWindow.ApplyResultsFilter();
            }
            else
            {
                _duplicateWindow.AdvancedSearchIncludes = includes;
                _duplicateWindow.AdvancedSearchExcludes = excludes;
                _duplicateWindow.ApplyDuplicateResultsFilter();
            }

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

            if (_mainWindow != null)
            {
                _mainWindow.AdvancedSearchIncludes = new List<string>();
                _mainWindow.AdvancedSearchExcludes = new List<string>();
                _mainWindow.ApplyResultsFilter();
            }
            else
            {
                _duplicateWindow.AdvancedSearchIncludes = new List<string>();
                _duplicateWindow.AdvancedSearchExcludes = new List<string>();
                _duplicateWindow.ApplyDuplicateResultsFilter();
            }
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
