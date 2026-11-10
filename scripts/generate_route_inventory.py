"""Imprime o inventario de rotas da API (secao "Endpoints principais" do README).

Le os Map*/MapGroup/RequireAuthorization/WithTags de HemodinksAPI.Api e traduz as politicas para perfis.
Atualize POL quando uma politica mudar em ApiServiceCollectionExtensions.Auth.cs.

    python3 scripts/generate_route_inventory.py > /tmp/rotas.md
"""
import glob
import os
import re
import sys
from pathlib import Path

def match_paren(s,i):
    d=0
    while i<len(s):
        c=s[i]
        if c=='"':
            i+=1
            while s[i]!='"':
                if s[i]=='\\': i+=1
                i+=1
        elif c=='(' : d+=1
        elif c==')':
            d-=1
            if d==0: return i
        i+=1
def chain_after(s,i):
    j=i; d=0
    while j<len(s):
        c=s[j]
        if c in '([{': d+=1
        elif c in ')]}':
            d-=1
            if d<0: break
        elif c==';' and d<=0: break
        j+=1
    return s[i:j]
def collect(root):
  rows=[]
  for f in sorted(glob.glob(root+'/HemodinksAPI.Api/**/*.cs',recursive=True)):
      if '/obj/' in f: continue
      s=open(f,encoding='utf-8-sig').read()
      groups={}
      for m in re.finditer(r'(?:var\s+(\w+)\s*=|return)\s*(\w+)\.MapGroup\("([^"]+)"\)',s):
          groups[m.group(1) or 'group']=(m.group(3),chain_after(s,m.end()))
      for m in re.finditer(r'(\w+)\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]*)"',s):
          g,verb,path=m.groups()
          close=match_paren(s,s.index('(',m.start()))
          chain=chain_after(s,close+1)
          gp,gchain=groups.get(g,('',''))
          tag=(re.findall(r'WithTags\("([^"]+)"\)',chain) or re.findall(r'WithTags\("([^"]+)"\)',gchain) or ['?'])[0]
          pols=[p.strip().strip('"') for p in re.findall(r'RequireAuthorization\(([^)]*)\)',gchain+chain)]
          anon='AllowAnonymous' in chain or 'AllowAnonymous' in gchain
          full=(gp+path).rstrip('/') or gp
          full=re.sub(r':int\}','}',full)
          rows.append(dict(verb=verb.upper(),path=full,tag=tag,pols=[p for p in pols if p],anon=anon,file=os.path.basename(f)))
  uniq={}
  for r in rows: uniq[(r['verb'],r['path'])]=r
  return list(uniq.values())


A,M,P,C,S,E='Adm','Méd','Pac','Ctrl','SA','Eq'
ALL=[A,M,P,C,S,E]
POL={
 'Administrador':[A,S],'SuperAdministrador':[S],'Equipe':[E],
 'UsuariosVisualizar':[A,S,E],
 'GrupoMedicoCadastrar':[A,S,C],'GrupoMedicoGerenciar':[A,S,C],
 'PacienteCadastrar':[A,S,C,M,E],'PacienteArquivosGerenciar':[A,S,M,C,E],'PacienteEditar':[A,S,M,C,E],
 'PacienteExcluir':[A,S,M],'PacientePendenciasVisualizar':[A,S,C,M],'PacienteObservacaoGerenciar':[A,S,M,C,E],
 'FaturamentoMedicoVisualizar':[A,S,M,C,E],'AtendimentoVisualizar':[A,S,M,C],'AtendimentoGerenciar':[A,S,M,C],
 'FaturamentoVisualizar':[A,S,M,C],'FaturamentoGerenciar':[A,S,C],'FinanceiroVisualizar':[A,S,C],'FinanceiroGerenciar':[A,S,C],
 'TabelaPrecoVisualizar':[A,S,M,C],'TabelaPrecoGerenciar':[A,S,C],
 'LicencaPolicies.PacientesVisualizar':[A,S,M,P,C,E],
}
LIC={'LicencaPolicies.DashboardVisualizar':'`Dashboard.Visualizar`','LicencaPolicies.CbhpmConsultar':'`Cbhpm.Consultar`','LicencaPolicies.PacientesGerenciar':'`Pacientes.Gerenciar`'}
ORDER=['Sessao','Users','Equipes - Autenticacao','Equipes','Plataforma - Clinicas','Plataforma - Auditoria','Clinicas - Publico','ConfiguracoesSistema','Legal','Privacy','Dashboard','Pacientes','Atendimentos cirurgicos','Faturamento medico','Faturamento','Contas a receber','Relatorios financeiros','Precos por convenio','GruposMedicos','Agenda e notificacoes','CBHPM','Hospitais','Convenios','OPME','Licencas','Exports','Monitoramento','?']
TITLE={'Sessao':'Sessão','Users':'Usuários','Equipes - Autenticacao':'Equipes — autenticação','Equipes':'Equipes','Plataforma - Clinicas':'Plataforma — clínicas','Plataforma - Auditoria':'Plataforma — auditoria','Clinicas - Publico':'Clínicas — público','ConfiguracoesSistema':'Configuração do sistema','Legal':'Aceites legais','Privacy':'Preferências de privacidade','Dashboard':'Dashboard','Pacientes':'Pacientes e cirurgias','Atendimentos cirurgicos':'Atendimentos cirúrgicos','Faturamento medico':'Faturamento médico','Faturamento':'Faturamento (lotes, glosas e recursos)','Contas a receber':'Contas a receber','Relatorios financeiros':'Relatórios financeiros','Precos por convenio':'Preços por convênio','GruposMedicos':'Grupos médicos','Agenda e notificacoes':'Agenda e notificações','CBHPM':'CBHPM','Hospitais':'Hospitais','Convenios':'Convênios','OPME':'OPME','Licencas':'Licenças','Exports':'Exportações','Monitoramento':'Monitoramento','?':'Infraestrutura'}
def who(r):
    if r['anon'] :
        if r['path'].startswith('/api/session/'): return 'Cookie de sessão (HttpOnly)'
        if r['path']=='/api/equipe-auth/identificar': return 'Login da equipe (rate limit)'
        return 'Público'
    sets=[];lic=[];sens=False
    for p in r['pols']:
        if p in POL: sets.append(set(POL[p]))
        elif p in LIC: lic.append(LIC[p])
        elif p=='EquipeOperacaoSensivel': sens=True
        else: sets.append({'?'+p})
    if r['tag']=='Plataforma - Clinicas' and not r['pols']: sets.append({A,S})
    allowed=set(ALL)
    for s in sets: allowed&=s
    txt=', '.join(x for x in ALL if x in allowed) if allowed!=set(ALL) else 'Autenticado'
    if lic: txt+=' + licença '+', '.join(lic)
    if sens and E in allowed: txt+=' (Eq: operador identificado)'
    return txt
def render(d):
  by={}
  for r in d: by.setdefault(r['tag'],[]).append(r)
  out=[]
  vo={'GET':0,'POST':1,'PUT':2,'DELETE':3}
  for t in ORDER:
      if t not in by: continue
      out.append(f"\n#### {TITLE[t]}\n\n| Método | Rota | Acesso |\n| --- | --- | --- |")
      for r in sorted(by[t],key=lambda r:(r['path'],vo[r['verb']])):
          out.append(f"| `{r['verb']}` | `{r['path']}` | {who(r)} |")
  return '\n'.join(out)


if __name__ == "__main__":
    root = str(Path(__file__).resolve().parents[1])
    routes = collect(root)
    print(render(routes))
    print(f"\n<!-- {len(routes)} rotas -->", file=sys.stderr)
