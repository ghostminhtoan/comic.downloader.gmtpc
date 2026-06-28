$content = Get-Content MainWindow.xaml -Raw -Encoding UTF8

$oldProgressBar = '                                            <!-- Downloading UI -->
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
                                                <ProgressBar Value="{Binding ProgressPercent}" Minimum="0" Maximum="100" Style="{StaticResource ProgressBarSuccess}" Height="20"/>
                                                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center">
                                                    <Button Content="⬇️" Click="BtnItemStart_Click" Tag="{Binding}" Style="{StaticResource CyberpunkButtonCyan}" Width="25" Height="20" Padding="0" FontSize="10" Margin="2,0" ToolTip="Resume" BorderThickness="0"/>
                                                    <Button Content="⏸️" Click="BtnItemPause_Click" Tag="{Binding}" Style="{StaticResource CyberpunkButtonPink}" Width="25" Height="20" Padding="0" FontSize="10" Margin="2,0" ToolTip="Pause" BorderThickness="0"/>
                                                    <Button Content="⏹️" Click="BtnItemStop_Click" Tag="{Binding}" Background="#ff4444" Foreground="White" Width="25" Height="20" Padding="0" FontSize="10" Margin="2,0" ToolTip="Stop" BorderThickness="0"/>
                                                </StackPanel>
                                            </Grid>'

$oldProgressBar = $oldProgressBar.Replace("`r`n", "`n")
$content = $content.Replace("`r`n", "`n")

$newProgressBar = '                                            <!-- Downloading UI -->
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

$content = $content.Replace($oldProgressBar, $newProgressBar)

Set-Content MainWindow.xaml -Value $content -Encoding UTF8
