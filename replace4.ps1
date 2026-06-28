$content = Get-Content MainWindow.xaml -Raw -Encoding UTF8

function ReplacePageInfo {
    param ($content, $prefix)
    
    $pattern = '(?s)<!-- Page Info Status -->.*?<Border Grid\.Row="3"[^>]*>.*?<TextBox[^>]*Name="txt' + $prefix + 'TotalPages"[^>]*>.*?</Grid>.*?</StackPanel>.*?</Border>\s*<!-- Page Range Input -->\s*<Grid Grid\.Row="4"[^>]*>.*?</Grid>'
    
    $replacement = '<!-- Page Info & Range Input -->
                                <Grid Grid.Row="3" Margin="0,0,0,20">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="10"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="10"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    
                                    <StackPanel Grid.Column="0">
                                        <TextBlock Text="TOTAL PAGE" Foreground="{StaticResource CyberpunkMutedTextBrush}" FontSize="11" FontWeight="Bold" Margin="0,0,0,6"/>
                                        <TextBox Name="txt' + $prefix + 'TotalPages" Text="1" Background="#0e121a" BorderBrush="{StaticResource CyberpunkBorderBrush}" Foreground="{StaticResource CyberpunkYellowBrush}" CaretBrush="{StaticResource CyberpunkYellowBrush}" Height="32" HorizontalContentAlignment="Center" TextChanged="Txt' + $prefix + 'TotalPages_TextChanged" IsReadOnly="True"/>
                                    </StackPanel>

                                    <StackPanel Grid.Column="2">
                                        <TextBlock Text="FROM PAGE" Foreground="{StaticResource CyberpunkMutedTextBrush}" FontSize="11" FontWeight="Bold" Margin="0,0,0,6"/>
                                        <TextBox Name="txt' + $prefix + 'PageFrom" Text="1" Background="#0e121a" BorderBrush="{StaticResource CyberpunkBorderBrush}" Foreground="{StaticResource CyberpunkTextBrush}" CaretBrush="{StaticResource CyberpunkCyanBrush}" Height="32" HorizontalContentAlignment="Center"/>
                                    </StackPanel>

                                    <StackPanel Grid.Column="4">
                                        <TextBlock Text="TO PAGE" Foreground="{StaticResource CyberpunkMutedTextBrush}" FontSize="11" FontWeight="Bold" Margin="0,0,0,6"/>
                                        <TextBox Name="txt' + $prefix + 'PageTo" Text="1" Background="#0e121a" BorderBrush="{StaticResource CyberpunkBorderBrush}" Foreground="{StaticResource CyberpunkTextBrush}" CaretBrush="{StaticResource CyberpunkCyanBrush}" Height="32" HorizontalContentAlignment="Center"/>
                                    </StackPanel>
                                </Grid>
                                <Grid Grid.Row="4" Height="0"/>'
                                
    return [System.Text.RegularExpressions.Regex]::Replace($content, $pattern, $replacement)
}

$content = ReplacePageInfo $content ""
$content = ReplacePageInfo $content "Nhentai"
$content = ReplacePageInfo $content "ViHentai"
$content = ReplacePageInfo $content "Truyenqq"

# Replace GreaterThanZeroConverter
$content = $content.Replace('<Window.Resources>', '<Window.Resources>' + [Environment]::NewLine + '        <local:GreaterThanZeroConverter x:Key="GreaterThanZeroConverter"/>')
$content = $content.Replace('Converter={x:Static local:GreaterThanZeroConverter.Instance}', 'Converter={StaticResource GreaterThanZeroConverter}')

# SelectionChanged
$content = $content.Replace('<TabControl Name="tabLeftPanel" Grid.Column="0" Grid.Row="0" Style="{StaticResource TabControlInLine}" Background="Transparent" BorderThickness="0">', '<TabControl Name="tabLeftPanel" Grid.Column="0" Grid.Row="0" Style="{StaticResource TabControlInLine}" Background="Transparent" BorderThickness="0" SelectionChanged="TabLeftPanel_SelectionChanged">')

# Progress bar layout
$progressBarPattern = '(?s)<!-- Downloading UI -->\s*<Grid>.*?<ProgressBar Value="\{Binding ProgressPercent\}" Minimum="0" Maximum="100" Style="\{StaticResource ProgressBarSuccess\}" Height="20"/>\s*<StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center">.*?<Button Content="⬇️"[^>]*>.*?<Button Content="⏸️" Click="BtnItemPause_Click"[^>]*>.*?<Button Content="⏹️"[^>]*>.*?</StackPanel>\s*</Grid>'

$progressBarReplacement = '<!-- Downloading UI -->
                                            <Grid>
                                                <Grid.Style>
                                                    <Style TargetType="Grid">
                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding IsDownloading}" Value="True">
                                                                <Setter Property="Visibility" Value="Visible"/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </Grid.Style>
                                                <Grid.RowDefinitions>
                                                    <RowDefinition Height="Auto"/>
                                                    <RowDefinition Height="Auto"/>
                                                </Grid.RowDefinitions>
                                                <StackPanel Grid.Row="0" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,2">
                                                    <Button Content="⬇️" Click="BtnItemStart_Click" Tag="{Binding}" Style="{StaticResource CyberpunkButtonCyan}" Width="25" Height="20" Padding="0" FontSize="10" Margin="2,0" ToolTip="Start" BorderThickness="0"/>
                                                    <Button Click="BtnItemPause_Click" Tag="{Binding}" Width="25" Height="20" Padding="0" FontSize="10" Margin="2,0" BorderThickness="0">
                                                        <Button.Style>
                                                            <Style TargetType="Button" BasedOn="{StaticResource CyberpunkButtonPink}">
                                                                <Setter Property="Content" Value="⏸️"/>
                                                                <Setter Property="ToolTip" Value="Pause"/>
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding IsPaused}" Value="True">
                                                                        <Setter Property="Content" Value="▶️"/>
                                                                        <Setter Property="ToolTip" Value="Resume"/>
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </Button.Style>
                                                    </Button>
                                                    <Button Content="⏹️" Click="BtnItemStop_Click" Tag="{Binding}" Background="#ff4444" Foreground="White" Width="25" Height="20" Padding="0" FontSize="10" Margin="2,0" ToolTip="Stop" BorderThickness="0"/>
                                                </StackPanel>
                                                <ProgressBar Grid.Row="1" Value="{Binding ProgressPercent}" Minimum="0" Maximum="100" Style="{StaticResource ProgressBarSuccess}" Height="8"/>
                                            </Grid>'

$content = [System.Text.RegularExpressions.Regex]::Replace($content, $progressBarPattern, $progressBarReplacement)

Set-Content MainWindow.xaml -Value $content -Encoding UTF8
