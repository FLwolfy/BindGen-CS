typedef signed char NativeObjCBool;

@protocol RootProtocol
- (int)rootValue:(int)value;
@end

@protocol ExtraProtocol
@end

@protocol CombinedProtocol <RootProtocol, ExtraProtocol>
@end

__attribute__((objc_root_class))
@interface Root
+ (instancetype)make;
- (float)measure;
@end

@interface Bag<T, U> : Root <CombinedProtocol>
@property int count;
@property (readwrite, assign) int explicitCount;
@property (readonly) float weight;
@property (nonatomic, copy, getter=displayName, setter=setDisplayName:) Root* name;
@property (nonatomic, weak) Root* weakTarget;
@property (atomic, strong) Root* strongTarget;
@property (nonatomic, unsafe_unretained) Root* unsafeTarget;
@property (nonatomic, retain) Root* retainedTarget;
@property (class, nonatomic, strong) Root* shared;
- (T)itemAt:(int)index;
- (U)secondAt:(int)index;
- (void)setItem:(T)item at:(int)index;
typedef T Element;
@end

@interface Bag (Extras)
- (int)extra;
@end

typedef float (^NativeBlock)(int index, int* value);
void consume_block(NativeBlock block);
