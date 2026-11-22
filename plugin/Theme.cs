using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace AIRaceEngineer
{
    /// <summary>
    /// Thème de la page du plugin : couleurs, boutons, interrupteurs, tuiles.
    /// Écrit en XAML (lu au démarrage) pour profiter des templates et animations WPF.
    /// </summary>
    internal static class Theme
    {
        public static readonly ResourceDictionary Resources = (ResourceDictionary)XamlReader.Parse(Xaml);

        public static Brush B(string key) => (Brush)Resources[key];
        public static Style S(string key) => (Style)Resources[key];
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <SolidColorBrush x:Key='Accent' Color='#2196F3'/>
  <SolidColorBrush x:Key='AccentSoft' Color='#262196F3'/>
  <SolidColorBrush x:Key='Ok' Color='#34D399'/>
  <SolidColorBrush x:Key='OkStrong' Color='#16A34A'/>
  <SolidColorBrush x:Key='OkSoft' Color='#1F34D399'/>
  <SolidColorBrush x:Key='Warn' Color='#FBBF24'/>
  <SolidColorBrush x:Key='WarnSoft' Color='#1FFBBF24'/>
  <SolidColorBrush x:Key='Bad' Color='#F87171'/>
  <SolidColorBrush x:Key='BadSoft' Color='#1FF87171'/>
  <SolidColorBrush x:Key='Card' Color='#0CFFFFFF'/>
  <SolidColorBrush x:Key='CardStrong' Color='#14FFFFFF'/>
  <SolidColorBrush x:Key='Line' Color='#1CFFFFFF'/>
  <SolidColorBrush x:Key='Text' Color='#F2FFFFFF'/>
  <SolidColorBrush x:Key='Muted' Color='#9EFFFFFF'/>
  <SolidColorBrush x:Key='Faint' Color='#66FFFFFF'/>

  <LinearGradientBrush x:Key='Hero' StartPoint='0,0' EndPoint='1,1'>
    <GradientStop Color='#FF15406B' Offset='0'/>
    <GradientStop Color='#FF102A45' Offset='0.45'/>
    <GradientStop Color='#FF161B22' Offset='1'/>
  </LinearGradientBrush>

  <!-- Boutons : un seul gabarit, des variantes de couleurs -->
  <Style x:Key='Btn' TargetType='Button'>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='Background' Value='#17FFFFFF'/>
    <Setter Property='BorderBrush' Value='#2EFFFFFF'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Padding' Value='14,7'/>
    <Setter Property='Margin' Value='0,0,8,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='7'/>
            <Border x:Name='hl' Background='White' Opacity='0' CornerRadius='7'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'
                              TextElement.Foreground='{TemplateBinding Foreground}'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='hl' Property='Opacity' Value='0.09'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='hl' Property='Opacity' Value='0.18'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='BtnPrimary' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='#2196F3'/>
    <Setter Property='BorderBrush' Value='#2196F3'/>
  </Style>
  <Style x:Key='BtnSuccess' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='#16A34A'/>
    <Setter Property='BorderBrush' Value='#16A34A'/>
  </Style>
  <Style x:Key='BtnGhost' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='Foreground' Value='#64B5F6'/>
    <Setter Property='Padding' Value='8,7'/>
  </Style>
  <Style x:Key='Chip' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='#262196F3'/>
    <Setter Property='BorderBrush' Value='#802196F3'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Padding' Value='10,4'/>
    <Setter Property='Margin' Value='0,6,6,0'/>
  </Style>

  <!-- Interrupteur (CheckBox) -->
  <Style x:Key='Toggle' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Border x:Name='track' Width='44' Height='24' CornerRadius='12' Background='#38FFFFFF'>
            <Ellipse x:Name='thumb' Width='18' Height='18' Fill='White' HorizontalAlignment='Left' Margin='3,0,3,0'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='track' Property='Background' Value='#2196F3'/>
              <Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='thumb' Property='Opacity' Value='0.88'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Tuile sélectionnable (RadioButton) : fournisseurs, langue, moteur de voix -->
  <Style x:Key='Tile' TargetType='RadioButton'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Foreground' Value='#F2FFFFFF'/>
    <Setter Property='Margin' Value='0,0,10,10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Grid>
            <Border x:Name='bd' Background='#0CFFFFFF' BorderBrush='#24FFFFFF' BorderThickness='1.5' CornerRadius='10'/>
            <Border x:Name='check' Width='18' Height='18' CornerRadius='9' Background='#2196F3' HorizontalAlignment='Right'
                    VerticalAlignment='Top' Margin='0,8,8,0' Visibility='Collapsed'>
              <TextBlock Text='&#xE73E;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='10' Foreground='White'
                         HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ContentPresenter Margin='14,11,30,11'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#17FFFFFF'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='bd' Property='BorderBrush' Value='#2196F3'/>
              <Setter TargetName='bd' Property='Background' Value='#222196F3'/>
              <Setter TargetName='check' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }
}
