$content = Get-Content MainWindow.xaml -Raw -Encoding UTF8
$content = $content.Replace("`r`n", "`n")

$newColumns = '                            <!-- Status & Progress Column -->
                            <DataGridTemplateColumn Header="STATUS &amp; PROGRESS" Width="180">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <Grid Margin="5,0">
                                            <!-- Standard Status (When not downloading) -->
                                            <TextBlock Text="{Binding Status}" 
                                                       FontWeight="Bold"
                                                       HorizontalAlignment="Center"
                                                       VerticalAlignment="Center">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock">
                                                        <Setter Property="Foreground" Value="{StaticResource CyberpunkCyanBrush}"/>
                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding IsDownloading}" Value="False">
                                                                <Setter Property="Visibility" Value="Visible"/>
                                                            </DataTrigger>
                                                            <DataTrigger Binding="{Binding Status}" Value="Completed">
                                                                <Setter Property="Foreground" Value="#00ff9d"/>
                                                            </DataTrigger>
                                                            <DataTrigger Binding="{Binding Status}" Value="Error">
                                                                <Setter Property="Foreground" Value="#ff4444"/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>
                                            
                                            <!-- Downloading UI -->
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
                                                <!-- Action Buttons for this item -->
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
                                                <!-- Progress Bar -->
                                                <ProgressBar Grid.Row="1" Value="{Binding ProgressPercent}" Minimum="0" Maximum="100" Style="{StaticResource ProgressBarSuccess}" Height="8"/>
                                            </Grid>
                                        </Grid>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>

                            <!-- Process Column -->
                            <DataGridTextColumn Header="PROCESS" Binding="{Binding CurrentProcess}" Width="150">
                                <DataGridTextColumn.ElementStyle>
                                    <Style TargetType="TextBlock">
                                        <Setter Property="Foreground" Value="{StaticResource CyberpunkTextBrush}"/>
                                        <Setter Property="Padding" Value="4,2"/>
                                        <Setter Property="TextWrapping" Value="Wrap"/>
                                    </Style>
                                </DataGridTextColumn.ElementStyle>
                            </DataGridTextColumn>

                            <!-- Errors Column -->
                            <DataGridTemplateColumn Header="" Width="65">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <Button Content="{Binding ErrorCount}" 
                                                Click="BtnShowErrors_Click"
                                                HorizontalAlignment="Center"
                                                Width="40" Height="22"
                                                FontSize="11" Padding="0">
                                            <Button.Style>
                                                <Style TargetType="Button" BasedOn="{StaticResource CyberpunkButtonCyan}">
                                                    <Setter Property="Visibility" Value="Collapsed"/>
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding ErrorCount, Converter={StaticResource GreaterThanZeroConverter}}" Value="True">
                                                            <Setter Property="Visibility" Value="Visible"/>
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </Button.Style>
                                        </Button>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                        </DataGrid.Columns>'

# Insert columns into dgResults
$oldColumnsEnd = "                            <!-- Action Buttons Column -->
                            <DataGridTemplateColumn Header=`"ACTIONS`" Width=`"90`">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <StackPanel Orientation=`"Horizontal`" HorizontalAlignment=`"Center`">
                                            <Button Content=`"🌐`" 
                                                    Tag=`"{Binding Link}`" 
                                                    Click=`"BtnOpenLinkInRow_Click`"
                                                    Width=`"30`" Height=`"28`" Margin=`"2,0`"
                                                    Style=`"{StaticResource CyberpunkButtonCyan}`"
                                                    FontSize=`"13`" Padding=`"0`"
                                                    ToolTip=`"Mở link trong trình duyệt`"/>
                                            <Button Content=`"⬇️`" 
                                                    Click=`"BtnDownloadSingleInRow_Click`"
                                                    Width=`"30`" Height=`"28`" Margin=`"2,0`"
                                                    Style=`"{StaticResource CyberpunkButtonPink}`"
                                                    FontSize=`"13`" Padding=`"0`"
                                                    ToolTip=`"Tải truyện này`"/>
                                        </StackPanel>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                        </DataGrid.Columns>"

$newColumnsContent = $oldColumnsEnd.Replace("                        </DataGrid.Columns>", "$newColumns")
$content = $content.Replace($oldColumnsEnd, $newColumnsContent)

# Remove dgDownloadQueue and its header
$queuePattern = '(?s)<!-- Download Queue Header -->.*?<DataGrid Grid\.Row="4"\s*Name="dgDownloadQueue".*?</DataGrid>'
$content = [System.Text.RegularExpressions.Regex]::Replace($content, $queuePattern, '')

Set-Content MainWindow.xaml -Value $content -Encoding UTF8
