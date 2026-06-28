import re
import sys

def merge_grids():
    with open('MainWindow.xaml', 'r', encoding='utf-8') as f:
        content = f.read()

    # 1. Update Grid.RowDefinitions
    old_row_defs = """                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/> <!-- Header -->
                        <RowDefinition Height="Auto"/> <!-- Filter & Sort Toolbar -->
                        <RowDefinition Height="*" />    <!-- DataGrid -->
                        <RowDefinition Height="Auto"/> <!-- Download Queue Header -->
                        <RowDefinition Height="150"/>  <!-- Download Queue DataGrid -->
                        <RowDefinition Height="Auto"/> <!-- Download Section -->
                    </Grid.RowDefinitions>"""
                    
    old_row_defs_alt = """                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/> <!-- Header -->
                        <RowDefinition Height="Auto"/> <!-- Filter & Sort Toolbar -->
                        <RowDefinition Height="*"/>    <!-- DataGrid -->
                        <RowDefinition Height="Auto"/> <!-- Download Queue Header -->
                        <RowDefinition Height="150"/>  <!-- Download Queue DataGrid -->
                        <RowDefinition Height="Auto"/> <!-- Download Section -->
                    </Grid.RowDefinitions>"""

    new_row_defs = """                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/> <!-- Header -->
                        <RowDefinition Height="Auto"/> <!-- Filter & Sort Toolbar -->
                        <RowDefinition Height="*"/>    <!-- DataGrid -->
                        <RowDefinition Height="Auto"/> <!-- Download Section -->
                    </Grid.RowDefinitions>"""

    if old_row_defs in content:
        content = content.replace(old_row_defs, new_row_defs)
    elif old_row_defs_alt in content:
        content = content.replace(old_row_defs_alt, new_row_defs)
    else:
        # Fallback regex
        content = re.sub(r'<RowDefinition Height="Auto"/> <!-- Download Queue Header -->.*?<RowDefinition Height="150"/>  <!-- Download Queue DataGrid -->\s*', '', content, flags=re.DOTALL)

    # 2. Update Download Section Grid.Row="5" to Grid.Row="3"
    content = content.replace(
        '<!-- Download Section -->\n                    <Border Grid.Row="5"',
        '<!-- Download Section -->\n                    <Border Grid.Row="3"'
    )
    content = content.replace(
        '<!-- Download Section -->\r\n                    <Border Grid.Row="5"',
        '<!-- Download Section -->\r\n                    <Border Grid.Row="3"'
    )

    # 3. Delete Download Queue Header and dgDownloadQueue
    # Find start of Download Queue Header
    header_start = content.find('<!-- Download Queue Header -->\n                    <Grid Grid.Row="3"')
    if header_start == -1:
        header_start = content.find('<!-- Download Queue Header -->\r\n                    <Grid Grid.Row="3"')
    
    if header_start != -1:
        # Find end of dgDownloadQueue
        queue_search = '<DataGrid Grid.Row="4"\n                              Name="dgDownloadQueue"'
        if queue_search not in content:
            queue_search = '<DataGrid Grid.Row="4"\r\n                              Name="dgDownloadQueue"'
        
        queue_start = content.find(queue_search, header_start)
        if queue_start != -1:
            # Find the closing </DataGrid> of dgDownloadQueue
            queue_end = content.find('</DataGrid>', queue_start)
            if queue_end != -1:
                # Remove the block entirely
                content = content[:header_start] + content[queue_end + 11:]

    # 4. Insert new columns into dgResults
    new_cols = """                            <!-- Status & Progress Column -->
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
                                                        <DataTrigger Binding="{Binding ErrorCount, Converter={x:Static local:GreaterThanZeroConverter.Instance}}" Value="True">
                                                            <Setter Property="Visibility" Value="Visible"/>
                                                            <Setter Property="Background" Value="#ff2222"/>
                                                            <Setter Property="BorderBrush" Value="#ff4444"/>
                                                            <Setter Property="Foreground" Value="White"/>
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </Button.Style>
                                        </Button>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                        </DataGrid.Columns>"""

    # We replace the closing </DataGrid.Columns> of dgResults
    # To be safe, we find the exact end of the "ACTIONS" column and append there
    action_col_end = """                                                    ToolTip="Tải truyện này"/>
                                        </StackPanel>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                        </DataGrid.Columns>"""
    
    if action_col_end not in content:
        action_col_end = action_col_end.replace('\n', '\r\n')
        
    content = content.replace(action_col_end, action_col_end.replace('                        </DataGrid.Columns>', new_cols))

    with open('MainWindow.xaml', 'w', encoding='utf-8') as f:
        f.write(content)

if __name__ == '__main__':
    merge_grids()
