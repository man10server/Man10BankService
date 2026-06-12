using Microsoft.AspNetCore.Routing.Constraints;

namespace Man10BankService.Validation;

// ルート制約 {uuid:uuid} として使う厳密UUID検証。一致しない場合はルートにマッチせず 404 となる。
// (本サービスでは UUID 不一致は「対象なし」として扱えるため許容。書式エラーを 400 にしたい箇所は
//  アクション側で UuidValidation.IsValid を併用する。)
public sealed class UuidRouteConstraint : RegexRouteConstraint
{
    public UuidRouteConstraint() : base(UuidValidation.Pattern)
    {
    }
}
