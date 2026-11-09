namespace Westworld.Runtime.Core;

public sealed record SlotObservation(int Slot,int Value,int Count,float Nutrition=0,bool CanEat=false);
public sealed record CellObservation(int X,int Y,int Z,string Visibility,int? Value);
public sealed record ActorObservation(Guid SessionId,Guid ActorId,Guid ObservationId,double Time,Position Position,float Health,float Food,SlotObservation[] Inventory,CellObservation[] Cells);
public sealed record MoveIntent(Guid SessionId,Guid ActorId,Guid ActionId,Guid ObservationId,float Right,float Forward,float Seconds);
public sealed record SkillResult(Guid SessionId,Guid ActorId,Guid ActionId,string Status,Position Start,Position End,double HorizontalDistance,double ForwardProgress,string Reason);

public sealed record EatIntent(Guid SessionId,Guid ActorId,Guid ActionId,Guid ObservationId,int Slot,int ExpectedValue);
public sealed record EatResult(Guid SessionId,Guid ActorId,Guid ActionId,string Status,int Slot,int CountBefore,int CountAfter,float FoodBefore,float FoodAfter,string Reason,long ItemsEatenBefore=0,long ItemsEatenAfter=0);
