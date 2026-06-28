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

$content = ReplacePageInfo $content "Nhentai"
$content = ReplacePageInfo $content "ViHentai"
$content = ReplacePageInfo $content "Truyenqq"

Set-Content MainWindow.xaml -Value $content -Encoding UTF8
