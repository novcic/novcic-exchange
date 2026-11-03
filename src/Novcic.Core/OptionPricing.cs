namespace Novcic.Core;

// Decimal-only Black–Scholes with zero carry. No binary floating-point financial inputs.
public static class OptionPricing
{
    public static decimal Sqrt(decimal x) { if(x<=0)return 0;var y=x>1?x:1;for(var i=0;i<100;i++){var n=(y+x/y)/2;if(Math.Abs(n-y)<0.000000000000000001m)return n;y=n;}return y; }
    static decimal Log(decimal x){if(x<=0)throw new ArgumentOutOfRangeException(nameof(x));decimal offset=0;while(x>2){x/=2;offset+=0.6931471805599453094172321215m;}while(x<1){x*=2;offset-=0.6931471805599453094172321215m;}var z=(x-1)/(x+1);var term=z;decimal sum=0;for(var n=1;n<200;n+=2){sum+=term/n;term*=z*z;}return 2*sum+offset;}
    static decimal Exp(decimal x){if(x< -60)return 0;if(x>0)return 1/Exp(-x);var k=0;while(x< -0.5m){x/=2;k++;}decimal sum=1,term=1;for(var n=1;n<60;n++){term*=x/n;sum+=term;}while(k-->0)sum*=sum;return sum;}
    static decimal Cdf(decimal x){if(x<0)return 1-Cdf(-x);var t=1/(1+0.2316419m*x);return 1-0.39894228040143267794m*Exp(-x*x/2)*t*(0.319381530m+t*(-0.356563782m+t*(1.781477937m+t*(-1.821255978m+t*1.330274429m))));}
    public static (decimal Price,decimal Delta) Value(decimal spot,decimal strike,decimal years,decimal iv,bool call)
    {
        if(spot<=0||strike<=0||years<=0||iv<=0)throw new ArgumentOutOfRangeException(nameof(spot));
        var vol=iv*Sqrt(years);var d1=(Log(spot/strike)+vol*vol/2)/vol;var d2=d1-vol;
        var value=call?spot*Cdf(d1)-strike*Cdf(d2):strike*Cdf(-d2)-spot*Cdf(-d1);
        return(Math.Max(call?Math.Max(0,spot-strike):Math.Max(0,strike-spot),value),call?Cdf(d1):Cdf(d1)-1);
    }
    public static decimal? Volatility(IReadOnlyList<OptionReference> rows,decimal strike,DateTime expiry,DateTime now)
    {
        var dates=rows.Where(r=>r.Time>=now.AddSeconds(-60)&&r.Time<=now.AddSeconds(5)&&r.Iv>0&&r.Iv<=5&&r.Expiry>now).GroupBy(r=>r.Expiry).OrderBy(g=>g.Key).ToList();
        decimal? At(IGrouping<DateTime,OptionReference> g){var sorted=g.OrderBy(r=>r.Strike).ToList();var lo=sorted.LastOrDefault(r=>r.Strike<=strike);var hi=sorted.FirstOrDefault(r=>r.Strike>=strike);if(lo==null||hi==null)return null;return lo.Strike==hi.Strike?lo.Iv:lo.Iv+(hi.Iv-lo.Iv)*(strike-lo.Strike)/(hi.Strike-lo.Strike);}
        var left=dates.LastOrDefault(g=>g.Key<=expiry);var right=dates.FirstOrDefault(g=>g.Key>=expiry);if(left==null||right==null)return null;
        var a=At(left);var b=At(right);if(a==null||b==null)return null;if(left.Key==right.Key)return a;
        var t=(decimal)(expiry-now).Ticks;var ta=(decimal)(left.Key-now).Ticks;var tb=(decimal)(right.Key-now).Ticks;
        return Sqrt((a.Value*a.Value*ta+(b.Value*b.Value*tb-a.Value*a.Value*ta)*(t-ta)/(tb-ta))/t);
    }
}
