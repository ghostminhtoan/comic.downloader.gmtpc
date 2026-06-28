using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ReplaceScript
{
    class Program
    {
        static void Main(string[] args)
        {
            string content = File.ReadAllText("MainWindow.xaml", System.Text.Encoding.UTF8);

            string oldRowDefs = @"                    <Grid.RowDefinitions>
                        <RowDefinition Height=""Auto""/> <!-- Header -->
                        <RowDefinition Height=""Auto""/> <!-- Filter & Sort Toolbar -->
                        <RowDefinition Height=""*""/>    <!-- DataGrid -->
                        <RowDefinition Height=""Auto""/> <!-- Download Queue Header -->
                        <RowDefinition Height=""150""/>  <!-- Download Queue DataGrid -->
                        <RowDefinition Height=""Auto""/> <!-- Download Section -->
                    </Grid.RowDefinitions>";
                    
            string newRowDefs = @"                    <Grid.RowDefinitions>
                        <RowDefinition Height=""Auto""/> <!-- Header -->
                        <RowDefinition Height=""Auto""/> <!-- Filter & Sort Toolbar -->
                        <RowDefinition Height=""*""/>    <!-- DataGrid -->
                        <RowDefinition Height=""Auto""/> <!-- Download Section -->
                    </Grid.RowDefinitions>";
            
            content = content.Replace(oldRowDefs, newRowDefs);
            // also try CRLF version if necessary
            content = content.Replace(oldRowDefs.Replace("\r\n", "\n"), newRowDefs.Replace("\r\n", "\n"));
            content = content.Replace(oldRowDefs.Replace("\n", "\r\n"), newRowDefs.Replace("\n", "\r\n"));

            content = content.Replace("<!-- Download Section -->\n                    <Border Grid.Row=\"5\"", "<!-- Download Section -->\n                    <Border Grid.Row=\"3\"");
            content = content.Replace("<!-- Download Section -->\r\n                    <Border Grid.Row=\"5\"", "<!-- Download Section -->\r\n                    <Border Grid.Row=\"3\"");

            int headerStart = content.IndexOf("<!-- Download Queue Header -->");
            if (headerStart != -1)
            {
                // Must be the ACTUAL download queue header, which is followed by Grid.Row="3"
                int actualHeaderStart = content.IndexOf("<!-- Download Queue Header -->\n                    <Grid Grid.Row=\"3\"");
                if (actualHeaderStart == -1) actualHeaderStart = content.IndexOf("<!-- Download Queue Header -->\r\n                    <Grid Grid.Row=\"3\"");
                
                if (actualHeaderStart != -1)
                {
                    int queueStart = content.IndexOf("Name=\"dgDownloadQueue\"", actualHeaderStart);
                    if (queueStart != -1)
                    {
                        int endGrid = content.IndexOf("</DataGrid>", queueStart);
                        if (endGrid != -1)
                        {
                            content = content.Substring(0, actualHeaderStart) + content.Substring(endGrid + 11);
                        }
                    }
                }
            }

            string newCols = @"                            <!-- Status & Progress Column -->
                            <DataGridTemplateColumn Header=""STATUS &amp; PROGRESS"" Width=""180"">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <Grid Margin=""5,0"">
                                            <!-- Standard Status (When not downloading) -->
                                            <TextBlock Text=""{Binding Status}"" 
                                                       FontWeight=""Bold""
                                                       HorizontalAlignment=""Center""
                                                       VerticalAlignment=""Center"">
                                                <TextBlock.Style>
                                                    <Style TargetType=""TextBlock"">
                                                        <Setter Property=""Foreground"" Value=""{StaticResource CyberpunkCyanBrush}""/>
                                                        <Setter Property=""Visibility"" Value=""Collapsed""/>
                                                        <Style.Triggers>
                                                            <DataTrigger Binding=""{Binding IsDownloading}"" Value=""False"">
                                                                <Setter Property=""Visibility"" Value=""Visible""/>
                                                            </DataTrigger>
                                                            <DataTrigger Binding=""{Binding Status}"" Value=""Completed"">
                                                                <Setter Property=""Foreground"" Value=""#00ff9d""/>
                                                            </DataTrigger>
                                                            <DataTrigger Binding=""{Binding Status}"" Value=""Error"">
                                                                <Setter Property=""Foreground"" Value=""#ff4444""/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>
                                            
                                            <!-- Downloading UI -->
                                            <Grid>
                                                <Grid.Style>
                                                    <Style TargetType=""Grid"">
                                                        <Setter Property=""Visibility"" Value=""Collapsed""/>
                                                        <Style.Triggers>
                                                            <DataTrigger Binding=""{Binding IsDownloading}"" Value=""True"">
                                                                <Setter Property=""Visibility"" Value=""Visible""/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </Grid.Style>
                                                <Grid.RowDefinitions>
                                                    <RowDefinition Height=""Auto""/>
                                                    <RowDefinition Height=""Auto""/>
                                                </Grid.RowDefinitions>
                                                <!-- Action Buttons for this item -->
                                                <StackPanel Grid.Row=""0"" Orientation=""Horizontal"" HorizontalAlignment=""Center"" Margin=""0,0,0,2"">
                                                    <Button Content=""⬇️"" Click=""BtnItemStart_Click"" Tag=""{Binding}"" Style=""{StaticResource CyberpunkButtonCyan}"" Width=""25"" Height=""20"" Padding=""0"" FontSize=""10"" Margin=""2,0"" ToolTip=""Start"" BorderThickness=""0""/>
                                                    <Button Click=""BtnItemPause_Click"" Tag=""{Binding}"" Width=""25"" Height=""20"" Padding=""0"" FontSize=""10"" Margin=""2,0"" BorderThickness=""0"">
                                                        <Button.Style>
                                                            <Style TargetType=""Button"" BasedOn=""{StaticResource CyberpunkButtonPink}"">
                                                                <Setter Property=""Content"" Value=""⏸️""/>
                                                                <Setter Property=""ToolTip"" Value=""Pause""/>
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding=""{Binding IsPaused}"" Value=""True"">
                                                                        <Setter Property=""Content"" Value=""▶️""/>
                                                                        <Setter Property=""ToolTip"" Value=""Resume""/>
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </Button.Style>
                                                    </Button>
                                                    <Button Content=""⏹️"" Click=""BtnItemStop_Click"" Tag=""{Binding}"" Background=""#ff4444"" Foreground=""White"" Width=""25"" Height=""20"" Padding=""0"" FontSize=""10"" Margin=""2,0"" ToolTip=""Stop"" BorderThickness=""0""/>
                                                </StackPanel>
                                                <!-- Progress Bar -->
                                                <ProgressBar Grid.Row=""1"" Value=""{Binding ProgressPercent}"" Minimum=""0"" Maximum=""100"" Style=""{StaticResource ProgressBarSuccess}"" Height=""8""/>
                                            </Grid>
                                        </Grid>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>

                            <!-- Process Column -->
                            <DataGridTextColumn Header=""PROCESS"" Binding=""{Binding CurrentProcess}"" Width=""150"">
                                <DataGridTextColumn.ElementStyle>
                                    <Style TargetType=""TextBlock"">
                                        <Setter Property=""Foreground"" Value=""{StaticResource CyberpunkTextBrush}""/>
                                        <Setter Property=""Padding"" Value=""4,2""/>
                                        <Setter Property=""TextWrapping"" Value=""Wrap""/>
                                    </Style>
                                </DataGridTextColumn.ElementStyle>
                            </DataGridTextColumn>

                            <!-- Errors Column -->
                            <DataGridTemplateColumn Header="""" Width=""65"">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <Button Content=""{Binding ErrorCount}"" 
                                                Click=""BtnShowErrors_Click""
                                                HorizontalAlignment=""Center""
                                                Width=""40"" Height=""22""
                                                FontSize=""11"" Padding=""0"">
                                            <Button.Style>
                                                <Style TargetType=""Button"" BasedOn=""{StaticResource CyberpunkButtonCyan}"">
                                                    <Setter Property=""Visibility"" Value=""Collapsed""/>
                                                    <Style.Triggers>
                                                        <DataTrigger Binding=""{Binding ErrorCount, Converter={x:Static local:GreaterThanZeroConverter.Instance}}"" Value=""True"">
                                                            <Setter Property=""Visibility"" Value=""Visible""/>
                                                            <Setter Property=""Background"" Value=""#ff2222""/>
                                                            <Setter Property=""BorderBrush"" Value=""#ff4444""/>
                                                            <Setter Property=""Foreground"" Value=""White""/>
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </Button.Style>
                                        </Button>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                        </DataGrid.Columns>";

            string endActionCol = @"                                                    ToolTip=""Tải truyện này""/>
                                        </StackPanel>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                        </DataGrid.Columns>";

            content = content.Replace(endActionCol, endActionCol.Replace("                        </DataGrid.Columns>", newCols));
            content = content.Replace(endActionCol.Replace("\r\n", "\n"), endActionCol.Replace("\r\n", "\n").Replace("                        </DataGrid.Columns>", newCols));

            File.WriteAllText("MainWindow.xaml", content, new System.Text.UTF8Encoding(true));
        }
    }
}
