$content = Get-Content MainWindow.xaml -Raw -Encoding UTF8
$content = $content.Replace("`r`n", "`n")

function ReplacePageInfo {
    param ($content, $prefix, $row1, $row2)
    
    $oldBlock = '                                <!-- Page Info Status -->
                                <Border Grid.Row="' + $row1 + '" Background="#0c0f17" BorderBrush="{StaticResource CyberpunkBorderBrush}" BorderThickness="1" Padding="12" CornerRadius="3" Margin="0,0,0,15">
                                    <StackPanel>
                                        <TextBlock Text="TARGET PAGE ANALYSIS" Foreground="{StaticResource CyberpunkPinkBrush}" FontSize="11" FontWeight="Bold" Margin="0,0,0,8"/>
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="100"/>
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Text="Total Pages Found:" Foreground="{StaticResource CyberpunkMutedTextBrush}" FontSize="13" VerticalAlignment="Center"/>
                                            <TextBox Grid.Column="1" Name="txt' + $prefix + 'TotalPages" Text="1" Background="#0e121a" BorderBrush="{StaticResource CyberpunkBorderBrush}" Foreground="{StaticResource CyberpunkYellowBrush}" CaretBrush="{StaticResource CyberpunkYellowBrush}" FontSize="13" FontWeight="Bold" Height="30" HorizontalContentAlignment="Center" TextChanged="Txt' + $prefix + 'TotalPages_TextChanged"/>
                                        </Grid>
                                    </StackPanel>
                                </Border>

                                <!-- Page Range Input -->
                                <Grid Grid.Row="' + $row2 + '" Margin="0,0,0,20">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="15"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    
                                    <StackPanel Grid.Column="0">
                                        <TextBlock Text="FROM PAGE" Foreground="{StaticResource CyberpunkMutedTextBrush}" FontSize="11" FontWeight="Bold" Margin="0,0,0,6"/>
                                        <TextBox Name="txt' + $prefix + 'PageFrom" Text="1" Background="#0e121a" BorderBrush="{StaticResource CyberpunkBorderBrush}" Foreground="{StaticResource CyberpunkTextBrush}" CaretBrush="{StaticResource CyberpunkCyanBrush}" Height="32" HorizontalContentAlignment="Center"/>
                                    </StackPanel>

                                    <StackPanel Grid.Column="2">
                                        <TextBlock Text="TO PAGE" Foreground="{StaticResource CyberpunkMutedTextBrush}" FontSize="11" FontWeight="Bold" Margin="0,0,0,6"/>
                                        <TextBox Name="txt' + $prefix + 'PageTo" Text="1" Background="#0e121a" BorderBrush="{StaticResource CyberpunkBorderBrush}" Foreground="{StaticResource CyberpunkTextBrush}" CaretBrush="{StaticResource CyberpunkCyanBrush}" Height="32" HorizontalContentAlignment="Center"/>
                                    </StackPanel>
                                </Grid>'

    $newBlock = '                                <!-- Page Info & Range Input -->
                                <Grid Grid.Row="' + $row1 + '" Margin="0,0,0,20">
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
                                <Grid Grid.Row="' + $row2 + '" Height="0"/>'
                                
    return $content.Replace($oldBlock, $newBlock)
}

$content = ReplacePageInfo -content $content -prefix "" -row1 "3" -row2 "4"
$content = ReplacePageInfo -content $content -prefix "Nhentai" -row1 "4" -row2 "5"
$content = ReplacePageInfo -content $content -prefix "ViHentai" -row1 "3" -row2 "4"
$content = ReplacePageInfo -content $content -prefix "Truyenqq" -row1 "3" -row2 "4"

Set-Content MainWindow.xaml -Value $content -Encoding UTF8
