using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonoHUD.App.Media;
using SonoHUD.App.ViewModels;
using SonoHUD.Core;

namespace SonoHUD.App.Views;

public partial class OnlineLyricsView : UserControl
{
    StudioViewModel? vm;
    LyricDocument? document;
    readonly List<LyricLineView> rows=[];
    readonly List<int> activeRows=[];
    double activeFrom=double.NaN,activeUntil=double.NaN;
    bool manual;
    bool presentationActive=true;
    int previous=-2;
    public OnlineLyricsView(){InitializeComponent();Loaded+=(_,_)=>Attach();Unloaded+=(_,_)=>Detach();DataContextChanged+=(_,_)=>{if(IsLoaded)Attach();};IsVisibleChanged+=(_,_)=>{if(IsVisible)RefreshFrame();};}
    void Attach(){Detach();vm=DataContext as StudioViewModel;if(vm is null)return;vm.Lyrics.Changed+=Refresh;vm.Lyrics.FrameChanged+=RefreshFrame;Refresh();}
    void Detach(){if(vm is null)return;vm.Lyrics.Changed-=Refresh;vm.Lyrics.FrameChanged-=RefreshFrame;vm=null;}
    internal event Action? LayoutPolicyChanged;
    internal void SetPresentationActive(bool value)
    {
        if(presentationActive==value)return;
        presentationActive=value;
        if(value)RefreshFrame();
    }
    internal void RefreshColors(){foreach(var row in rows)row.RefreshColors();}
    void Refresh()
    {
        if(vm is null)return;var lyrics=vm.Lyrics;var doc=lyrics.Document;
        Mode.Text=doc.Format switch{LyricFormat.Words=>"단어 동기화",LyricFormat.Lines=>"줄 동기화",LyricFormat.Plain=>lyrics.Status==LyricsStatus.Demo?"자체 샘플 · 일반 가사":"일반 가사 · 시간 정보 없음",_=>""};
        Notice.SetResourceReference(TextBlock.ForegroundProperty,lyrics.Status==LyricsStatus.Error ? "StudioError" : "StudioMuted");
        Notice.Text=lyrics.Message;Notice.Visibility=Notice.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        RetryButton.IsEnabled=CandidatesButton.IsEnabled=vm.OnlineLyricsEnabled && lyrics.Track is not null && lyrics.Status is not (LyricsStatus.Searching or LyricsStatus.Demo);
        Candidates.ItemsSource=lyrics.Candidates;CandidatesPanel.Visibility=lyrics.Candidates.Count>0?Visibility.Visible:Visibility.Collapsed;
        OffsetPanel.Visibility=doc.Timed?Visibility.Visible:Visibility.Collapsed;
        OffsetLabel.Text=lyrics.OffsetMs==0?"기본 타이밍":$"{Math.Abs(lyrics.OffsetMs)/1000d:0.00}초 "+(lyrics.OffsetMs>0?"빠르게":"느리게");
        if(lyrics.Status==LyricsStatus.Candidates)Notice.Text="가사 옵션에서 곡에 맞는 가사를 선택하세요.";
        if(!ReferenceEquals(document,doc))
        {
            document=doc;activeRows.Clear();activeFrom=activeUntil=double.NaN;lastFrame=new(null,-1,false);Lines.Children.Clear();rows.Clear();manual=false;previous=-2;
            foreach(var line in doc.Lines){var row=new LyricLineView(line);rows.Add(row);Lines.Children.Add(row);}
            ReadingPanel.Visibility=doc.Lines.Count>0?Visibility.Visible:Visibility.Collapsed;Reader.ScrollToTop();
        }
        LayoutPolicyChanged?.Invoke();RefreshFrame();
    }
    LyricFrame lastFrame=new(null,-1,false);
    void RefreshFrame()
    {
        if(vm is null || document is null || !presentationActive || !IsVisible)return;var frame=vm.Lyrics.Frame;
        if(frame.Position is double position)
        {
            if(!(position>=activeFrom && position<activeUntil))
            {
                activeRows.Clear();activeFrom=position;activeUntil=double.PositiveInfinity;
                for(int i=0;i<rows.Count;i++){
                    var line=rows[i].Line;
                    if(line.Start>position){rows[i].Update(frame,false);activeUntil=Math.Min(activeUntil,line.Start.Value);continue;}
                    if(!frame.Active(document,i)){rows[i].Update(frame,false,line.Start.HasValue);continue;}
                    activeRows.Add(i);
                    if(line.End>position)activeUntil=Math.Min(activeUntil,line.End.Value);
                }
            }
            foreach(int i in activeRows)rows[i].Update(frame,true);
        }
        else if(lastFrame.Position.HasValue){
            foreach(var row in rows)row.Update(frame,false);
            activeRows.Clear();activeFrom=activeUntil=double.NaN;
        }
        if(frame.Uncertain)Mode.Text="재생 위치 확인 중 · 강조 일시 유지";
        else Mode.Text=document.Format switch{LyricFormat.Words=>"단어 동기화",LyricFormat.Lines=>"줄 동기화",LyricFormat.Plain=>vm.Lyrics.Status==LyricsStatus.Demo?"자체 샘플 · 일반 가사":"일반 가사 · 시간 정보 없음",_=>""};
        if(previous!=frame.Current)
        {
            var motion=LyricsMotion.Between(vm.Document.Overlay.Animation,!manual && vm.Media.State.IsPlaying && SystemParameters.ClientAreaAnimation,lastFrame,frame);
            previous=frame.Current;
            if(!manual)ScrollCurrent();
            if(motion.Milliseconds>0 && previous>=0 && previous<rows.Count){
                var row=rows[previous];var t=new System.Windows.Media.TranslateTransform();row.RenderTransform=t;
                var duration=TimeSpan.FromMilliseconds(motion.Milliseconds);
                t.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,new System.Windows.Media.Animation.DoubleAnimation(motion.Distance,0,duration));
                row.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(motion.StartOpacity,1,duration));
            }
        }
        lastFrame=frame;
        ReturnButton.Visibility=manual && document.Timed?Visibility.Visible:Visibility.Collapsed;
    }
    void ScrollCurrent()
    {
        if(previous<0 || previous>=rows.Count)return;
        Reader.UpdateLayout();var row=rows[previous];if(!row.IsArrangeValid)return;
        double top=row.TransformToAncestor(Lines).Transform(new(0,0)).Y;
        Reader.ScrollToVerticalOffset(Math.Max(0,top-Reader.ViewportHeight*.34));
    }
    void ToggleOptions(object sender,RoutedEventArgs e)
    {
        bool open=OptionsPanel.Visibility!=Visibility.Visible;
        OptionsPanel.Visibility=open?Visibility.Visible:Visibility.Collapsed;
        Options.Content=open?"옵션 닫기":"가사 옵션";
    }
    void MarkManual(){manual=true;ReturnButton.Visibility=document?.Timed==true?Visibility.Visible:Visibility.Collapsed;}
    void ManualWheel(object sender,MouseWheelEventArgs e)=>MarkManual();
    void ManualMouse(object sender,MouseButtonEventArgs e)=>MarkManual();
    void ManualKey(object sender,KeyEventArgs e){if(e.Key is Key.Up or Key.Down or Key.PageDown or Key.PageUp or Key.Home or Key.End)MarkManual();}
    void ReturnToCurrent(object sender,RoutedEventArgs e){manual=false;ScrollCurrent();RefreshFrame();}
    async void Retry(object sender,RoutedEventArgs e){if(vm is not null)await vm.Lyrics.Retry();}
    async void FindCandidates(object sender,RoutedEventArgs e){if(vm is not null)await vm.Lyrics.Retry(true);}
    async void ApplyCandidate(object sender,RoutedEventArgs e){if(vm is not null && Candidates.SelectedItem is LyricRecord record){await vm.Lyrics.Select(record.Id);if(vm.Lyrics.Status==LyricsStatus.Ready && OptionsPanel.Visibility==Visibility.Visible)ToggleOptions(sender,e);}}
    void Faster(object sender,RoutedEventArgs e){if(vm is not null){vm.Lyrics.Offset(vm.Lyrics.OffsetMs+250);vm.ObserveLyricsFrame();}}
    void Slower(object sender,RoutedEventArgs e){if(vm is not null){vm.Lyrics.Offset(vm.Lyrics.OffsetMs-250);vm.ObserveLyricsFrame();}}
    void ResetOffset(object sender,RoutedEventArgs e){vm?.Lyrics.Offset(0);vm?.ObserveLyricsFrame();}
}
