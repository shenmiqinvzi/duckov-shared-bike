using System;
using System.Linq;
using DuckovSharedBike.Domain;
using UnityEngine;

namespace DuckovSharedBike
{
    internal sealed partial class BikeWorldController
    {
        private int _invoicePage;
        private bool _invoiceHistory;
        private bool _invoiceHintShown;
        private GUIStyle? _buttonStyle;
        private GUIStyle? _titleStyle;
        private Font? _styleFont;
        internal void Draw(GUIStyle label,float width,float height)
        {
            EnsureStyles(label);
            var button=_buttonStyle!;
            var title=_titleStyle!;
            var rental=_runtime.Engine?.CurrentRental;
            if (rental!=null)
            {
                var text=(rental.RideState==RideState.Riding ? "骑行中" : "车辆未归还")+"｜租赁 "+Duration(rental.ElapsedActiveMs)
                    +"｜费用 "+_runtime.Engine!.CurrentFee!.Value.Total+"元\n"+MountKeyText+" 上下车   "+LossKeyText+" 报失回收   下车后继续计费";
                GUI.Box(new Rect(16,height-112,540,92),GUIContent.none);
                GUI.Label(new Rect(30,height-101,510,78),text,label);
            }
            else if (_summoning && _summonUntil>Time.unscaledTime)
            {
                var remain=Mathf.CeilToInt(_summonUntil-Time.unscaledTime);
                GUI.Box(new Rect(16,height-112,540,92),GUIContent.none);
                GUI.Label(new Rect(30,height-101,510,78),"召车中｜自行车正在路上，约 "+remain+" 秒送达\n"+SummonKeyText+" 召车（¥"+_settings.Summon.Cost+"）｜送达后按"+MountKeyText+"上车",label);
            }
            if (Alive && Ready)
            {
                var station=NearestStation(3);
                if (station!=null && _dialogStation==null)
                    GUI.Label(new Rect(16,height-155,580,36),"附近："+station.Name+"｜"+StationKeyText+" 打开租还面板",label);
                else if (_bike!=null && rental!=null && Vector3.Distance(Actor!.transform.position,_bike.transform.position)<2.5f && rental.RideState==RideState.Parked)
                    GUI.Label(new Rect(16,height-155,580,36),"你租赁的自行车｜"+MountKeyText+" 上车",label);
                if (_settings.Ui.ShowWorldLabels) DrawWorldLabels(label,width,height);
            }
            if (_toastUntil>Time.unscaledTime)
            {
                GUI.Box(new Rect((width-660)*0.5f,height-225,660,62),GUIContent.none);
                GUI.Label(new Rect((width-636)*0.5f,height-217,636,48),_toast,label);
            }
            if (_runtime.SavePending)
                GUI.Label(new Rect(16,height-42,700,32),"单车资料尚未保存到文件，正在重试；请等待此提示消失再退出。",label);
            if (_dialogStation!=null) DrawStation(label,title,button,width,height);
            if (_confirmLoss) DrawLoss(label,title,button,width,height);
            if (LevelManager.Instance!=null && LevelManager.Instance.IsBaseLevel && Duckov.UI.StorageDock.Instance!=null
                && Duckov.UI.StorageDock.Instance.open) DrawInvoices(label,title,button,width,height);
            else _invoiceHintShown=false;
        }

        private void EnsureStyles(GUIStyle label)
        {
            if (_buttonStyle!=null && _titleStyle!=null && _styleFont==label.font) return;
            _styleFont=label.font;
            _buttonStyle=new GUIStyle(GUI.skin.button) { font=label.font,fontSize=16,wordWrap=true };
            _titleStyle=new GUIStyle(label) { fontSize=20,fontStyle=FontStyle.Bold };
        }

        private void DrawWorldLabels(GUIStyle label,float width,float height)
        {
            var camera=Camera.main;
            var actor=Actor;
            if (camera==null || actor==null) return;
            // Labels come from saved station data, not from the generated model, so the player can always
            // find a station even if a renderer or marker fails on this machine.
            if (_runtime.World!=null && !string.IsNullOrEmpty(_mapId) && !string.IsNullOrEmpty(_sceneName))
            {
                foreach (var station in _runtime.World.Stations.Where(s=>s.MapId==_mapId && s.SceneName==_sceneName))
                {
                    var position=SafeGround.Vector(station.Position);
                    var distance=Vector3.Distance(actor.transform.position,position);
                    if (distance>60) continue;
                    DrawWorldLabel(camera,position+Vector3.up*2.5f,station.Name+"  "+Mathf.CeilToInt(distance)+"米",label,width,height);
                }
            }
            if (_bike!=null && !IsRiding(actor))
            {
                var distance=Vector3.Distance(actor.transform.position,_bike.transform.position);
                if (distance<=40) DrawWorldLabel(camera,_bike.transform.position+Vector3.up*1.4f,"你的共享单车  "+Mathf.CeilToInt(distance)+"米",label,width,height);
            }
            // Native tunnels/manholes are easy to lose after a long ride; label the nearest two so the
            // player can always find a way back.
            var nearbyTravel=_nativeTravelPoints
                .Where(point=>point!=null)
                .Select(point=>new { Point=point, Distance=Vector3.Distance(actor.transform.position,point.transform.position) })
                .Where(entry=>entry.Distance<=80)
                .OrderBy(entry=>entry.Distance)
                .Take(2);
            foreach (var entry in nearbyTravel)
                DrawWorldLabel(camera,entry.Point.transform.position+Vector3.up*2.2f,"返回通道  "+Mathf.CeilToInt(entry.Distance)+"米",label,width,height);
        }

        private static void DrawWorldLabel(Camera camera,Vector3 world,string text,GUIStyle label,float width,float height)
        {
            var point=camera.WorldToViewportPoint(world);
            if (point.z<=0 || point.x<0 || point.x>1 || point.y<0 || point.y>1) return;
            var x=point.x*width-120; var y=(1-point.y)*height;
            GUI.Box(new Rect(x,y,240,30),GUIContent.none);
            GUI.Label(new Rect(x+6,y+2,228,26),text,label);
        }

        private void DrawStation(GUIStyle label,GUIStyle title,GUIStyle button,float width,float height)
        {
            var x=(width-550)*0.5f; var y=(height-400)*0.5f;
            GUI.Box(new Rect(x,y,550,400),GUIContent.none);
            var station=_runtime.World?.Stations.SingleOrDefault(s=>s.Id==_dialogStation);
            GUI.Label(new Rect(x+20,y+15,510,34),station?.Name ?? "共享单车站",title);
            GUI.Label(new Rect(x+20,y+61,510,206),
                "200元/小时，不足1小时按1小时计费。\n下车后继续计费，暂停、退出及加载不计时。\n骑行无视负重并加速（速度 x"+RideSpeedMultiplier.ToString("0.00")+"）。\n骑行不能战斗、冲刺、吃药或吃东西。\n未还车不能撤离，也不能租第二辆。\n普通通道先下车，单向跨区先还车。\n死亡自动回收，另赔偿2000元。\n还车或报失时自动扣款，现金不足可在站点或马蜂自提点补付。\n不在站点时按"+SummonKeyText+"可付费召车（¥"+_settings.Summon.Cost+"），车从最近站点赶来。",label);
            var engine=_runtime.Engine;
            if (engine==null) GUI.Label(new Rect(x+20,y+271,510,43),"当前业务资料暂不可用，请查看日志。",label);
            else if (engine.CurrentRental!=null)
            {
                GUI.Label(new Rect(x+20,y+269,510,38),"本次租赁费："+engine.CurrentFee!.Value.Total+"元",label);
                if (GUI.Button(new Rect(x+20,y+313,320,43),"归还当前车辆",button))
                {
                    try { Return(_dialogStation!); } catch (Exception ex) { _log("Return interrupted: "+ex); Notify("还车保存尚未完成，请保留日志并稍后重试。",true); }
                }
            }
            else if (engine.HasUnpaidOrders)
                GUI.Label(new Rect(x+20,y+269,510,86),"你有未支付订单，请在任意站点还车时补付，或到马蜂自提点付款后再租车。",label);
            else if (GUI.Button(new Rect(x+20,y+313,320,43),"租赁一辆自行车",button)) Rent(_dialogStation!);
            if (GUI.Button(new Rect(x+355,y+313,175,43),"关闭",button)) _dialogStation=null;
        }

        private void DrawLoss(GUIStyle label,GUIStyle title,GUIStyle button,float width,float height)
        {
            var x=(width-520)*0.5f; var y=(height-250)*0.5f;
            GUI.Box(new Rect(x,y,520,250),GUIContent.none);
            GUI.Label(new Rect(x+20,y+15,480,34),"报失并回收车辆",title);
            var rent=_runtime.Engine?.CurrentFee?.RentFee ?? 0;
            GUI.Label(new Rect(x+20,y+60,480,108),"车辆会立即回收并结束租赁。\n租赁费 "+rent+"元 + 回收赔偿2000元。\n合计 "+(rent+2000)+"元，确认后立即自动扣款。",label);
            if (GUI.Button(new Rect(x+20,y+182,230,44),"确认报失",button))
            {
                _confirmLoss=false;
                try { CloseRecovery(CloseReason.ReportedLost); } catch (Exception ex) { _log("Report interrupted: "+ex); Notify("回收保存尚未完成，请保留日志。",true); }
            }
            if (GUI.Button(new Rect(x+270,y+182,230,44),"取消",button)) _confirmLoss=false;
        }

        private void DrawInvoices(GUIStyle label,GUIStyle title,GUIStyle button,float width,float height)
        {
            var engine=_runtime.Engine;
            if (engine==null) return;
            var snapshot=engine.Snapshot();
            if (snapshot.Orders.Count==0) return;
            var pending=snapshot.Orders.Count(o=>o.PaymentStatus!=PaymentStatus.Paid);
            if (pending>0 && !_invoiceHintShown)
            {
                _invoiceHintShown=true;
                Notify("有"+pending+"笔共享单车账单，点击右侧账单即扣款。",true);
            }
            var orders=snapshot.Orders.Where(o=>_invoiceHistory ? o.PaymentStatus==PaymentStatus.Paid : o.PaymentStatus!=PaymentStatus.Paid).Reverse().ToArray();
            var pages=Math.Max(1,(orders.Length+1)/2); _invoicePage=Mathf.Clamp(_invoicePage,0,pages-1);
            var x=_settings.Ui.InvoicePanelSide.Trim().ToLowerInvariant()=="left" ? 10 : Math.Max(10,width-435);
            var y=Math.Max(10,Math.Min(80,height-640));
            GUI.Box(new Rect(x,y,420,625),GUIContent.none);
            GUI.Label(new Rect(x+16,y+12,388,32),"共享单车结算订单",title);
            if (GUI.Button(new Rect(x+16,y+49,190,36),"待付款（"+pending+"）",button)) { _invoiceHistory=false; _invoicePage=0; }
            if (GUI.Button(new Rect(x+214,y+49,190,36),"已付款历史",button)) { _invoiceHistory=true; _invoicePage=0; }
            GUI.Label(new Rect(x+16,y+92,388,30),_runtime.PaymentUnavailable ? "付款记录异常，请保留日志；不会再次扣款。" : "可用现金："+Duckov.Economy.EconomyManager.Cash+"元｜点击账单即扣款",label);
            var row=0;
            if (orders.Length==0) GUI.Label(new Rect(x+16,y+145,388,70),_invoiceHistory ? "暂无已付款订单。" : "单车订单已全部结清，可以再次租车。",label);
            foreach (var order in orders.Skip(_invoicePage*2).Take(2))
            {
                var top=y+134+row*211;
                var rental=snapshot.Rentals.Single(r=>r.RentalId==order.RentalId);
                var number=order.OrderId.Length>14 ? order.OrderId.Substring(order.OrderId.Length-8) : order.OrderId;
                GUI.Label(new Rect(x+16,top,388,162),
                    "订单 #"+number+"｜"+CloseText(order.CloseReason)+"\n租车："+StationName(rental.RentStationId)
                    +"\n结束："+(rental.ReturnStationId==null ? CloseText(order.CloseReason) : StationName(rental.ReturnStationId))
                    +"\n租赁 "+Duration(order.DurationMs)+"｜计费 "+order.BilledHours+"小时\n租赁费 "+order.RentFee+"元｜额外费用 "+order.AdditionalFee+"元"
                    +"\n合计 "+order.Total+"元｜"+(order.PaymentStatus==PaymentStatus.Paid ? "已支付" : order.PaymentStatus==PaymentStatus.Pending ? "待恢复付款" : "待付款")
                    +"\n"+(_invoiceHistory ? "付款时间 "+DisplayTime(order.PaidUtc) : "结束时间 "+DisplayTime(rental.ClosedUtc)),label);
                if (!_invoiceHistory && !_runtime.PaymentUnavailable && GUI.Button(new Rect(x+16,top+162,388,38),order.PaymentStatus==PaymentStatus.Pending ? "补付账单（恢复付款）" : "补付账单 "+order.Total+"元",button))
                {
                    try
                    {
                        var result=_runtime.Pay(order.OrderId);
                        _log("Payment order="+order.OrderId+" code="+result.Code+" pending="+engine.HasUnpaidOrders);
                        if (result.Code==RuleCode.InsufficientCash)
                        {
                            _sounds.ShortChime();
                            var cash=Duckov.Economy.EconomyManager.Cash;
                            Notify("无法提取：需支付"+order.Total+"元，当前现金"+cash+"元，还差"+Math.Max(0,order.Total-cash)+"元。",true);
                        }
                        else
                        {
                            if (result.Succeeded || result.Code==RuleCode.AlreadyPaid) _sounds.PaidChime();
                            Notify(result.Succeeded || result.Code==RuleCode.AlreadyPaid
                                ? (engine.HasUnpaidOrders ? "付款成功，仍有其他订单待支付。" : "付款成功，已恢复租车资格。")
                                : RuleText(result.Code),true);
                        }
                    }
                    catch (Exception ex) { _log("Payment interrupted: "+ex); Notify("付款结果需要核查，请保留模组日志。",true); }
                }
                row++;
            }
            if (pages>1)
            {
                if (GUI.Button(new Rect(x+16,y+577,100,35),"上一页",button)) _invoicePage=Math.Max(0,_invoicePage-1);
                GUI.Label(new Rect(x+146,y+577,130,35),(_invoicePage+1)+" / "+pages,label);
                if (GUI.Button(new Rect(x+304,y+577,100,35),"下一页",button)) _invoicePage=Math.Min(pages-1,_invoicePage+1);
            }
        }

        private string StationName(string id)=>_runtime.World?.Stations.SingleOrDefault(s=>s.Id==id)?.Name
            ?? _blueprints.Sites.SingleOrDefault(s=>s.Id==id)?.Name ?? "旧版站点（"+(id.Length>6 ? id.Substring(id.Length-6) : id)+"）";
        private static string DisplayTime(string? utc)=>DateTimeOffset.TryParse(utc,out var time) ? time.ToLocalTime().ToString("MM-dd HH:mm") : "旧版记录";
        private static string CloseText(CloseReason reason)=>reason==CloseReason.Returned ? "站点归还"
            : reason==CloseReason.DeathRecovery ? "死亡回收" : reason==CloseReason.ReportedLost ? "主动报失" : "系统回收（无额外赔偿）";

        private static string Duration(long milliseconds)
        {
            var seconds=milliseconds/1000;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:00}:{1:00}:{2:00}",seconds/3600,seconds/60%60,seconds%60);
        }
        private static string RuleText(RuleCode code)
        {
            switch (code)
            {
                case RuleCode.ActiveRentalExists: return "你还有一辆自行车未归还，请继续使用或归还当前车辆。";
                case RuleCode.UnpaidOrders: return "你有未支付订单，请在还车/报失时补付，或到马蜂自提点付款。";
                case RuleCode.PersistenceFailed: return "付款保存尚未完成，订单已保留，请稍后恢复结算。";
                case RuleCode.PaymentUncertain: return "付款结果需要核查，订单仍保留，请保留日志。";
                case RuleCode.PaymentDeclined: return "现金扣款未成功，订单仍保留。";
                case RuleCode.PaymentBusy: return "当前无法扣款，请稍后再点一次；同一笔账单不会重复扣款。";
                default: return "当前操作未完成，请稍后重试。";
            }
        }
    }
}
