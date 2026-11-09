using Westworld.Scapi.Adapter;
int n=0;void Eq(string a,string b){if(a!=b)throw new Exception(a+" != "+b);n++;}
foreach(var v in new[]{double.NaN,double.PositiveInfinity,-1,360}){try{BoundedNavigationRules.Validate(v,3,12);throw new Exception("accepted bearing");}catch(ArgumentOutOfRangeException){n++;}}
foreach(var v in new[]{double.NaN,0,.9,4.1,double.PositiveInfinity}){try{BoundedNavigationRules.Validate(90,v,12);throw new Exception("accepted distance");}catch(ArgumentOutOfRangeException){n++;}}
foreach(var v in new[]{double.NaN,0,12.1,double.PositiveInfinity}){try{BoundedNavigationRules.Validate(90,3,v);throw new Exception("accepted budget");}catch(ArgumentOutOfRangeException){n++;}}
BoundedNavigationRules.Validate(0,1,1);BoundedNavigationRules.Validate(359,4,12);n+=2;
Eq(BoundedNavigationRules.Next(2.1,1,12,1,true),"continue");Eq(BoundedNavigationRules.Next(.3,3,12,3,true),"arrived");Eq(BoundedNavigationRules.Next(.5,4,12,4,true),"segment_budget");Eq(BoundedNavigationRules.Next(.1,1,12,1,false),"segment_failed");Eq(BoundedNavigationRules.Next(.1,12,12,3,true),"wall_budget");Eq(BoundedNavigationRules.Next(double.NaN,1,12,1,true),"invalid_state");
Console.WriteLine($"{n}/{n} budget checks passed; no native lifecycle coverage claimed");
