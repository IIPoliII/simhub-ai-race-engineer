using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace AIRaceEngineer
{
    /// <summary>
    /// Thème de la page du plugin. Direction : le muret des stands la nuit.
    /// Chrome neutre (rampe graphite par-dessus le fond de SimHub), un seul accent utilisé rarement,
    /// chiffres et phrases de l'ingé en police mono comme un journal radio.
    /// Espacements sur une grille de 4 px, rayons 8 (contrôles) et 12 (cartes).
    /// </summary>
    internal static class Theme
    {
        public static readonly ResourceDictionary Resources = (ResourceDictionary)XamlReader.Parse(Xaml);

        public static Brush B(string key) => (Brush)Resources[key];
        public static Style S(string key) => (Style)Resources[key];
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <!-- Rampe neutre : surfaces en blanc translucide sur le fond sombre de SimHub -->
  <SolidColorBrush x:Key='Card' Color='#0AFFFFFF'/>
  <SolidColorBrush x:Key='Raised' Color='#12FFFFFF'/>
  <SolidColorBrush x:Key='Hover' Color='#1AFFFFFF'/>
  <SolidColorBrush x:Key='Line' Color='#14FFFFFF'/>
  <SolidColorBrush x:Key='Text' Color='#EDEDED'/>
  <SolidColorBrush x:Key='Text2' Color='#A1A1A6'/>
  <SolidColorBrush x:Key='Muted' Color='#85858B'/>

  <!-- Un accent (bleu SimHub, assombri pour 4.5:1 avec du texte blanc) -->
  <SolidColorBrush x:Key='Accent' Color='#1976D2'/>
  <SolidColorBrush x:Key='AccentLine' Color='#64B5F6'/>

  <!-- Couleurs sémantiques, adoucies -->
  <SolidColorBrush x:Key='Ok' Color='#4CC38A'/>
  <SolidColorBrush x:Key='Warn' Color='#E5A84D'/>
  <SolidColorBrush x:Key='Bad' Color='#E5484D'/>
  <SolidColorBrush x:Key='OkSoft' Color='#144CC38A'/>
  <SolidColorBrush x:Key='BadSoft' Color='#14E5484D'/>

  <Style x:Key='Btn' TargetType='Button'>
    <Setter Property='Foreground' Value='#EDEDED'/>
    <Setter Property='Background' Value='#12FFFFFF'/>
    <Setter Property='BorderBrush' Value='#14FFFFFF'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Setter Property='Padding' Value='12,6'/>
    <Setter Property='Margin' Value='0,0,8,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'/>
            <Border x:Name='hl' Background='White' Opacity='0' CornerRadius='8'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'
                              TextElement.Foreground='{TemplateBinding Foreground}'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='hl' Property='Opacity' Value='0.06'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='hl' Property='Opacity' Value='0.12'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#64B5F6'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='BtnPrimary' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='Background' Value='#1976D2'/>
    <Setter Property='BorderBrush' Value='#1976D2'/>
  </Style>
  <Style x:Key='BtnSuccess' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='Background' Value='#15803D'/>
    <Setter Property='BorderBrush' Value='#15803D'/>
  </Style>
  <Style x:Key='BtnGhost' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Foreground' Value='#A1A1A6'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='Padding' Value='8,6'/>
  </Style>
  <Style x:Key='BtnHelp' TargetType='Button'>
    <Setter Property='Foreground' Value='#64B5F6'/>
    <Setter Property='Width' Value='22'/>
    <Setter Property='Height' Value='22'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Border x:Name='bd' Background='#1A64B5F6' BorderBrush='#6664B5F6' BorderThickness='1' CornerRadius='11'/>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='0,0,0,1'
                              TextElement.Foreground='{TemplateBinding Foreground}'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#3364B5F6'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Background' Value='#4D64B5F6'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Chip' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='FontFamily' Value='Cascadia Mono, Consolas'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Padding' Value='8,4'/>
    <Setter Property='Margin' Value='0,8,8,0'/>
  </Style>

  <!-- Interrupteur -->
  <Style x:Key='Toggle' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Border x:Name='track' Width='36' Height='20' CornerRadius='10' Background='#24FFFFFF'>
            <Ellipse x:Name='thumb' Width='14' Height='14' Fill='#EDEDED' HorizontalAlignment='Left' Margin='3,0,3,0'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='track' Property='Background' Value='#EDEDED'/>
              <Setter TargetName='thumb' Property='Fill' Value='#141415'/>
              <Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='thumb' Property='Opacity' Value='0.85'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Curseur (volumes) : rail fin, partie remplie dans l'accent, pastille comme celle de l'interrupteur -->
  <Style x:Key='Slider' TargetType='Slider'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
    <Setter Property='IsMoveToPointEnabled' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Grid Height='24' Background='Transparent'>
            <Border Height='4' CornerRadius='2' Background='#24FFFFFF' VerticalAlignment='Center' Margin='7,0'/>
            <Track x:Name='PART_Track'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False' IsTabStop='False'>
                  <RepeatButton.Template>
                    <ControlTemplate TargetType='RepeatButton'>
                      <Grid Background='Transparent'>
                        <Border Height='4' CornerRadius='2' Background='#64B5F6' VerticalAlignment='Center' Margin='7,0,-7,0'/>
                      </Grid>
                    </ControlTemplate>
                  </RepeatButton.Template>
                </RepeatButton>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False' IsTabStop='False'>
                  <RepeatButton.Template>
                    <ControlTemplate TargetType='RepeatButton'>
                      <Grid Background='Transparent'/>
                    </ControlTemplate>
                  </RepeatButton.Template>
                </RepeatButton>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Width='14' Height='14'>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Ellipse x:Name='dot' Fill='#EDEDED' Stroke='#141415' StrokeThickness='1'/>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='dot' Property='Opacity' Value='0.85'/></Trigger>
                        <Trigger Property='IsDragging' Value='True'><Setter TargetName='dot' Property='Fill' Value='#64B5F6'/></Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Segment sélectionnable (fournisseur, langue, moteur de voix) -->
  <Style x:Key='Segment' TargetType='RadioButton'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Foreground' Value='#A1A1A6'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Margin' Value='0,0,8,8'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='bd' Background='Transparent' BorderBrush='#14FFFFFF' BorderThickness='1' CornerRadius='8' Padding='12,6'>
            <ContentPresenter VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#0AFFFFFF'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='#12FFFFFF'/>
              <Setter TargetName='bd' Property='BorderBrush' Value='#64B5F6'/>
              <Setter Property='Foreground' Value='#EDEDED'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }
}
