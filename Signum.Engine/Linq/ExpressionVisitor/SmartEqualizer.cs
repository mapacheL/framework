﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using Signum.Utilities;
using Signum.Entities;
using Signum.Utilities.Reflection;
using Signum.Engine.Maps;
using Signum.Utilities.ExpressionTrees;
using Signum.Engine.Properties;
using Signum.Entities.Reflection;

namespace Signum.Engine.Linq
{
    internal static class SmartEqualizer
    {
        static ConstantExpression True = Expression.Constant(true);
        static ConstantExpression False = Expression.Constant(false);


        public static Expression EqualNullableGroupBy(Expression e1, Expression e2)
        {
            return Expression.Or(Expression.Equal(e1.Nullify(), e2.Nullify()),
                Expression.And(new IsNullExpression(e1), new IsNullExpression(e2)));
        }

        public static Expression EqualNullable(Expression e1, Expression e2)
        {
            if (e1.Type.IsNullable() == e2.Type.IsNullable())
                return Expression.Equal(e1, e2);

            return Expression.Equal(e1.Nullify(), e2.Nullify());
        }

        public static Expression PolymorphicEqual(Expression exp1, Expression exp2)
        {
            if (exp1.NodeType == ExpressionType.New && exp2.NodeType == ExpressionType.New)
            {
                return (exp1 as NewExpression).Arguments.ZipStrict(
                       (exp2 as NewExpression).Arguments, (o, i) => SmartEqualizer.PolymorphicEqual(o, i)).Aggregate((a, b) => Expression.And(a, b));
            }

            return EntityEquals(exp1, exp2) ?? TypeEquals(exp1, exp2) ?? EqualNullable(exp1, exp2);
        }

        private static Expression TypeEquals(Expression exp1, Expression exp2)
        {
            if (exp1.Type != typeof(Type) || exp2.Type != typeof(Type))
                return null;

            if(exp1.NodeType == ExpressionType.Conditional)
                return DispachConditionalTypes((ConditionalExpression)exp1, exp2);

            if(exp2.NodeType == ExpressionType.Conditional)
                return DispachConditionalTypes((ConditionalExpression)exp2, exp1);

            if(exp1.NodeType == ExpressionType.Constant)
            {
                if(exp2.NodeType == ExpressionType.Constant) return ConstantConstantEquals((ConstantExpression)exp1, (ConstantExpression)exp2);
                else if(exp2.NodeType == (ExpressionType)DbExpressionType.TypeId) return ConstantTypeEquals((ConstantExpression)exp1, (TypeIdExpression)exp2);

            }
            else if(exp1.NodeType == (ExpressionType)DbExpressionType.TypeId)
            {
                if (exp2.NodeType == ExpressionType.Constant) return ConstantTypeEquals((ConstantExpression)exp2, (TypeIdExpression)exp1);
                else if (exp2.NodeType == (ExpressionType)DbExpressionType.TypeId) return TypeTypeEquals((TypeIdExpression)exp1, (TypeIdExpression)exp2);
            }

            throw new InvalidOperationException("Impossible to resolve '{0}' equals '{1}'".Formato(exp1.NiceToString(), exp2.NiceToString()));
        }

        private static Expression DispachConditionalTypes(ConditionalExpression ce, Expression exp)
        {
            return SmartOr(SmartAnd(ce.Test, TypeEquals(ce.IfTrue, exp)), TypeEquals(ce.IfFalse, exp));
        }

        private static Expression SmartAnd(Expression e1, Expression e2)
        {
            if (e1 == True)
                return e2;

            if (e2 == True)
                return e1;

            if (e1 == False || e2 == False)
                return False;

            return Expression.And(e1, e2); 
        }

        private static Expression SmartOr(Expression e1, Expression e2)
        {
            if (e1 == False)
                return e2;

            if (e2 == False)
                return e1;

            if (e1 == True || e2 == True)
                return True;

            return Expression.Or(e1, e2);
        }

        private static Expression ConstantTypeEquals(ConstantExpression c, TypeIdExpression ti)
        {
            return Expression.Equal(QueryBinder.TypeConstant((Type)c.Value), ti.Column);
        }

        private static Expression ConstantConstantEquals(ConstantExpression c1, ConstantExpression c2)
        {
            if (c1.Value == null)
            {
                if (c2.Value == null)
                    return True;
                else
                    return False;
            }
            else
            {
                if (c2.Value == null)
                    return False;

                if (c1.Value.Equals(c2.Value))
                    return True;
                else
                    return False;
            }
        }

        private static Expression TypeTypeEquals(TypeIdExpression t1, TypeIdExpression t2)
        {
            return Expression.Equal(t1.Column, t2.Column);
        }

        internal static Expression TypeIn(Expression typeExpr, IEnumerable<Type> collection)
        {
            if (collection.IsNullOrEmpty())
                return False;

            if (typeExpr.NodeType == ExpressionType.Conditional)
                return DispachConditionalTypesIn((ConditionalExpression)typeExpr, collection);

            if (typeExpr.NodeType == ExpressionType.Constant)
            {
                Type type = (Type)((ConstantExpression)typeExpr).Value;

                return collection.Contains(type) ? True : False;
            }

            if (typeExpr.NodeType == (ExpressionType)DbExpressionType.TypeId)
                return InExpression.FromValues(((TypeIdExpression)typeExpr).Column, collection.Select(t => (object)QueryBinder.TypeId(t)).ToArray());

            throw new InvalidOperationException("Impossible to resolve '{0}' in '{1}'".Formato(typeExpr.NiceToString(), collection.ToString(t=>t.TypeName(), ", ")));
        }

        private static Expression DispachConditionalTypesIn(ConditionalExpression ce, IEnumerable<Type> collection)
        {
            return SmartOr(SmartAnd(ce.Test, TypeIn(ce.IfTrue, collection)), TypeIn(ce.IfFalse, collection));
        }

        internal static Expression EntityIn(Expression newItem, IEnumerable<IdentifiableEntity> collection)
        {
            if (collection.IsEmpty())
                return False;

            Dictionary<Type, object[]> entityIDs = collection.AgGroupToDictionary(a => a.GetType(), gr => gr.Select(a => (object)(a.IdOrNull ?? int.MaxValue)).ToArray());

            return EntityIn(newItem, entityIDs);
        }

        internal static Expression EntityIn(LiteReferenceExpression liteReference, IEnumerable<Lite> collection)
        {
            if (collection.IsEmpty())
                return False;

            Dictionary<Type, object[]> entityIDs = collection.AgGroupToDictionary(a => a.RuntimeType, gr => gr.Select(a => (object)(a.IdOrNull ?? int.MaxValue)).ToArray());

            return EntityIn(liteReference.Reference, entityIDs); 
        }

        static Expression EntityIn(Expression newItem, Dictionary<Type, object[]> entityIDs)
        {
            FieldInitExpression fie = newItem as FieldInitExpression;
            if (fie != null)
                return InExpression.FromValues(fie.ExternalId, entityIDs.TryGetC(fie.Type) ?? new object[0]);

            ImplementedByExpression ib = newItem as ImplementedByExpression;
            if (ib != null)
                return ib.Implementations.ToDictionary(a => a.Type, a => a.Field).JoinDictionary(entityIDs, (t, f, values) => Expression.And(
                    new IsNotNullExpression(f.ExternalId),
                    InExpression.FromValues(f.ExternalId, values))).Values.Aggregate((a, b) => Expression.Or(a, b));

            ImplementedByAllExpression iba = newItem as ImplementedByAllExpression;
            if (iba != null)
                return entityIDs.Select(kvp => Expression.And(
                    EqualNullable(QueryBinder.TypeConstant(kvp.Key), iba.TypeId.Column),
                    InExpression.FromValues(iba.Id, kvp.Value))).Aggregate((a, b) => Expression.Or(a, b));

            throw new InvalidOperationException("EntityIn not defined for newItem of type {0}".Formato(newItem.Type.Name));
        }

        public static Expression EntityEquals(Expression e1, Expression e2)
        {
            e1 = ConstantToEntity(e1) ?? e1;
            e2 = ConstantToEntity(e2) ?? e2; 

            if (e1 is LiteReferenceExpression || e2 is LiteReferenceExpression )
            {
                e1 = e1.IsNull() ? e1 : ((LiteReferenceExpression)e1).Reference;
                e2 = e2.IsNull() ? e2 : ((LiteReferenceExpression)e2).Reference;
            }

            var tE1 = (DbExpressionType)e1.NodeType;
            var tE2 = (DbExpressionType)e2.NodeType;

            if (tE1 == DbExpressionType.EmbeddedFieldInit && e2.IsNull())
                return EmbeddedNullEquals((EmbeddedFieldInitExpression)e1);
            if (tE2 == DbExpressionType.EmbeddedFieldInit && e1.IsNull())
                return EmbeddedNullEquals((EmbeddedFieldInitExpression)e2);

            if (tE1 == DbExpressionType.FieldInit)
                if (tE2 == DbExpressionType.FieldInit) return FieFieEquals((FieldInitExpression)e1, (FieldInitExpression)e2);
                else if (tE2 == DbExpressionType.ImplementedBy) return FieIbEquals((FieldInitExpression)e1, (ImplementedByExpression)e2);
                else if (tE2 == DbExpressionType.ImplementedByAll) return FieIbaEquals((FieldInitExpression)e1, (ImplementedByAllExpression)e2);
                else if (e2.IsNull()) return EqualsToNull(((FieldInitExpression)e1).ExternalId);
                else return null;
            else if (tE1 == DbExpressionType.ImplementedBy)
                if (tE2 == DbExpressionType.FieldInit) return FieIbEquals((FieldInitExpression)e2, (ImplementedByExpression)e1);
                else if (tE2 == DbExpressionType.ImplementedBy) return IbIbEquals((ImplementedByExpression)e1, (ImplementedByExpression)e2);
                else if (tE2 == DbExpressionType.ImplementedByAll) return IbIbaEquals((ImplementedByExpression)e1, (ImplementedByAllExpression)e2);
                else if (e2.IsNull()) return ((ImplementedByExpression)e1).Implementations.Select(a => EqualsToNull(a.Field.ExternalId)).Aggregate((a, b) => Expression.And(a, b));
                else return null;
            else if (tE1 == DbExpressionType.ImplementedByAll)
                if (tE2 == DbExpressionType.FieldInit) return FieIbaEquals((FieldInitExpression)e2, (ImplementedByAllExpression)e1);
                else if (tE2 == DbExpressionType.ImplementedBy) return IbIbaEquals((ImplementedByExpression)e2, (ImplementedByAllExpression)e1);
                else if (tE2 == DbExpressionType.ImplementedByAll) return IbaIbaEquals((ImplementedByAllExpression)e1, (ImplementedByAllExpression)e2);
                else if (e2.IsNull()) return EqualsToNull(((ImplementedByAllExpression)e1).Id);
                else return null;
            else if (e1.IsNull())
                if (tE2 == DbExpressionType.FieldInit) return EqualsToNull(((FieldInitExpression)e2).ExternalId);
                else if (tE2 == DbExpressionType.ImplementedBy) return ((ImplementedByExpression)e2).Implementations.Select(a => EqualsToNull(a.Field.ExternalId)).Aggregate((a, b) => Expression.And(a, b));
                else if (tE2 == DbExpressionType.ImplementedByAll) return EqualsToNull(((ImplementedByAllExpression)e2).Id);
                else if (e2.IsNull()) return True;
                else return null;

            else return null;
        }

        static Expression EmbeddedNullEquals(EmbeddedFieldInitExpression efie)
        {
            if (efie.HasValue == null)
                return False; 

            return Expression.Not(efie.HasValue);
        }

        static Expression FieFieEquals(FieldInitExpression fie1, FieldInitExpression fie2)
        {
            if (fie1.Type == fie2.Type)
                return EqualNullable(fie1.ExternalId, fie2.ExternalId);
            else
                return False;
        }

        static Expression FieIbEquals(FieldInitExpression fie, ImplementedByExpression ib)
        {
            var imp = ib.Implementations.SingleOrDefault(i => i.Type == fie.Type);
            if (imp == null)
                return False;

            return EqualNullable(imp.Field.ExternalId, fie.ExternalId); 
        }

        static Expression FieIbaEquals(FieldInitExpression fie, ImplementedByAllExpression iba)
        {
            return Expression.And(EqualNullable(fie.ExternalId, iba.Id), EqualNullable(QueryBinder.TypeConstant(fie.Type), iba.TypeId.Column));
        }

        static Expression IbIbEquals(ImplementedByExpression ib, ImplementedByExpression ib2)
        {
            var list = ib.Implementations.Join(ib2.Implementations, i => i.Type, j => j.Type, (i, j) => EqualNullable(i.Field.ExternalId, j.Field.ExternalId)).ToList();
            if(list.Count == 0)
                return False;
            return list.Aggregate((e1, e2) => Expression.Or(e1, e2));
        }

        static Expression IbIbaEquals(ImplementedByExpression ib, ImplementedByAllExpression iba)
        {
            var list = ib.Implementations.Select(i => Expression.And(
                EqualNullable(iba.Id, i.Field.ExternalId),
                EqualNullable(iba.TypeId.Column, QueryBinder.TypeConstant(i.Field.Type)))).ToList();

            if (list.Count == 0)
                return False;

            return list.Aggregate((e1, e2) => Expression.Or(e1, e2));
        }


        static Expression IbaIbaEquals(ImplementedByAllExpression iba, ImplementedByAllExpression iba2)
        {
            return Expression.And(EqualNullable(iba.Id, iba2.Id), EqualNullable(iba.TypeId.Column, iba2.TypeId.Column)); 
        }

        static Expression EqualsToNull(Expression exp)
        {
            return new IsNullExpression(exp);
        }

        public static Expression ConstantToEntity(Expression expression)
        {
            ConstantExpression c = expression as ConstantExpression;
            if (c == null)
                return null;

            if (c.Value == null)
                return c;

            if (c.Type.IsIIdentifiable())
            {
                return ToFieldInitExpression((IdentifiableEntity)c.Value);// podria ser null y lo meteriamos igualmente
            }
            else if (c.Type.IsLite())
            {
                return ToLiteReferenceExpression((Lite)c.Value);
            }

            return null;
        }

        static Expression ToLiteReferenceExpression(Lite lite)
        {
            Expression id = Expression.Constant(lite.IdOrNull ?? int.MinValue);

            Type liteType = lite.GetType();

            FieldInitExpression fie = new FieldInitExpression(lite.RuntimeType, null, id, ProjectionToken.External);

            Type staticType = Reflector.ExtractLite(liteType);
            Expression reference = staticType == fie.Type? (Expression)fie: 
                new ImplementedByExpression(staticType, 
                    new[] { new ImplementationColumnExpression(fie.Type, (FieldInitExpression)fie) }.ToReadOnly());

            return new LiteReferenceExpression(liteType,
                reference, id, Expression.Constant(lite.ToStr), Expression.Constant(lite.RuntimeType));
        }

        static Expression ToFieldInitExpression(IIdentifiable ei)
        {
            return new FieldInitExpression(
                ei.GetType(),
                null,
                Expression.Constant(ei.IdOrNull ?? int.MinValue),
                ProjectionToken.External);
        }
    }
}
